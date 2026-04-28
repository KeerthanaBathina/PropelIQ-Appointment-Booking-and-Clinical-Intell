using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.AiAudit.Models;

namespace UPACIP.Service.AiAudit;

/// <summary>
/// AI audit logging service and background consumer (US_080 task_002, AIR-S04, AC-3, AC-4).
///
/// <para>
/// <b>Architecture:</b> Implements both <see cref="IAiAuditService"/> (public API) and
/// <see cref="BackgroundService"/> (channel consumer) so the same singleton instance
/// serves write requests and drains the audit queue without allocation overhead.
/// Register via:
/// <code>
/// services.AddSingleton&lt;AiAuditService&gt;();
/// services.AddSingleton&lt;IAiAuditService&gt;(sp => sp.GetRequiredService&lt;AiAuditService&gt;());
/// services.AddHostedService(sp => sp.GetRequiredService&lt;AiAuditService&gt;());
/// </code>
/// </para>
///
/// <para>
/// <b>Channel design:</b> A bounded <see cref="Channel{T}"/> (capacity 1,000) decouples
/// the AI Gateway hot path from database I/O.  When the channel is full (back-pressure),
/// incoming entries are dropped and a warning is logged — the AI request pipeline is
/// never blocked (NFR-030 graceful fallback).
/// </para>
///
/// <para>
/// <b>Cursor-based pagination:</b> Cursors encode <c>(CreatedAt, Id)</c> as
/// <c>base64({createdAt:O}|{id:N})</c>.  Results are ordered by <c>CreatedAt DESC</c>
/// then <c>Id DESC</c> for stable pages across concurrent inserts.
/// </para>
///
/// <para>
/// <b>PII policy:</b> Entries are persisted verbatim.  The caller (AiAuditLoggingMiddleware)
/// is responsible for ensuring <see cref="AiAuditLogEntry.Prompt"/> is post-PII-redacted.
/// </para>
/// </summary>
public sealed class AiAuditService : BackgroundService, IAiAuditService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int  ChannelCapacity      = 1_000;
    private const char CursorSeparator      = '|';
    private const int  BatchSize            = 50;   // Max rows committed per DB round-trip.

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly Channel<AiAuditLogEntry>     _channel;
    private readonly IServiceScopeFactory         _scopeFactory;
    private readonly ILogger<AiAuditService>      _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AiAuditService(
        IServiceScopeFactory    scopeFactory,
        ILogger<AiAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        // BoundedChannelFullMode.DropWrite ensures the writer never blocks —
        // the gateway request path is never held waiting for audit persistence.
        _channel = Channel.CreateBounded<AiAuditLogEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode       = BoundedChannelFullMode.DropWrite,
            SingleReader   = true,   // Only the BackgroundService consumer reads.
            SingleWriter   = false,  // Multiple AI Gateway threads may write concurrently.
        });
    }

    // ── IAiAuditService ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask LogAiInteractionAsync(
        AiAuditLogEntry  entry,
        CancellationToken ct = default)
    {
        // TryWrite is non-blocking; returns false only when the channel is full.
        if (!_channel.Writer.TryWrite(entry))
        {
            _logger.LogWarning(
                "AiAuditService: channel full — audit entry dropped. " +
                "RequestType={RequestType} UserId={UserId}",
                entry.RequestType, entry.UserId);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<AiAuditQueryResult> QueryAuditLogsAsync(
        AiAuditQueryFilter filter,
        CancellationToken  ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pageSize = filter.EffectivePageSize;

        // ── Build base query with conditional filters ─────────────────────────
        var query = db.AiAuditLogs.AsNoTracking();

        if (filter.DateFrom.HasValue)
            query = query.Where(e => e.CreatedAt >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(e => e.CreatedAt <= filter.DateTo.Value);

        if (!string.IsNullOrWhiteSpace(filter.ModelVersion))
            query = query.Where(e => e.ModelVersion == filter.ModelVersion);

        if (!string.IsNullOrWhiteSpace(filter.RequestType))
            query = query.Where(e => e.RequestType == filter.RequestType);

        if (filter.ConfidenceMin.HasValue)
            query = query.Where(e => e.ConfidenceScore >= filter.ConfidenceMin.Value);

        if (filter.ConfidenceMax.HasValue)
            query = query.Where(e => e.ConfidenceScore <= filter.ConfidenceMax.Value);

        // ── Count before cursor application ───────────────────────────────────
        // Run count on the filtered (non-paged) query for accurate totals.
        var totalCount = await query.LongCountAsync(ct);

        // ── Apply cursor (keyset pagination, DESC order) ──────────────────────
        if (!string.IsNullOrWhiteSpace(filter.Cursor))
        {
            var (cursorDate, cursorId) = DecodeCursor(filter.Cursor);
            // Next page: rows where createdAt < cursorDate,
            //  OR       : createdAt == cursorDate AND id < cursorId.
            query = query.Where(e =>
                e.CreatedAt < cursorDate
                || (e.CreatedAt == cursorDate && e.Id.CompareTo(cursorId) < 0));
        }

        // ── Fetch pageSize + 1 to detect HasMore ─────────────────────────────
        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore   = rows.Count > pageSize;
        var pageItems = hasMore ? rows.Take(pageSize).ToList() : rows;

        string? nextCursor = hasMore
            ? EncodeCursor(pageItems[^1].CreatedAt, pageItems[^1].Id)
            : null;

        return new AiAuditQueryResult
        {
            Items      = pageItems.Select(MapToDto).ToList(),
            NextCursor = nextCursor,
            TotalCount = totalCount,
            HasMore    = hasMore,
        };
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <summary>
    /// Background consumer loop — reads audit entries from the channel in batches
    /// and persists them to the <c>ai_audit_logs</c> table.
    /// Stops cleanly when <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AiAuditService: background consumer started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for at least one entry.
                await _channel.Reader.WaitToReadAsync(stoppingToken);

                // Drain up to BatchSize entries for a single DB round-trip.
                var batch = new List<AiAuditLogEntity>(BatchSize);
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var entry))
                {
                    batch.Add(MapToEntity(entry));
                }

                if (batch.Count == 0) continue;

                await PersistBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — drain remaining entries before exiting.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AiAuditService: unhandled error in consumer loop. Retrying after delay.");

                // Pause briefly to avoid a tight error loop; do NOT crash the host.
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        // ── Graceful shutdown: flush remaining entries ─────────────────────────
        _channel.Writer.TryComplete();
        var remaining = new List<AiAuditLogEntity>(BatchSize);
        while (_channel.Reader.TryRead(out var entry))
        {
            remaining.Add(MapToEntity(entry));
            if (remaining.Count >= BatchSize)
            {
                await PersistBatchAsync(remaining, CancellationToken.None);
                remaining.Clear();
            }
        }
        if (remaining.Count > 0)
            await PersistBatchAsync(remaining, CancellationToken.None);

        _logger.LogInformation("AiAuditService: background consumer stopped.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task PersistBatchAsync(
        IReadOnlyList<AiAuditLogEntity> batch,
        CancellationToken               ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.AiAuditLogs.AddRangeAsync(batch, ct);
            await db.SaveChangesAsync(ct);

            _logger.LogDebug("AiAuditService: persisted {Count} audit entries.", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiAuditService: failed to persist batch of {Count} audit entries. " +
                "Entries will be dropped — audit logging must not block AI responses.",
                batch.Count);
        }
    }

    // ── Cursor encoding / decoding ────────────────────────────────────────────

    private static string EncodeCursor(DateTime createdAt, Guid id)
    {
        var raw = $"{createdAt:O}{CursorSeparator}{id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateTime createdAt, Guid id) DecodeCursor(string cursor)
    {
        try
        {
            var raw   = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var sep   = raw.LastIndexOf(CursorSeparator);
            if (sep < 0) throw new FormatException("Cursor separator missing.");

            var dateStr = raw[..sep];
            var idStr   = raw[(sep + 1)..];

            return (DateTime.Parse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    Guid.Parse(idStr));
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid or corrupted pagination cursor.", nameof(cursor), ex);
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static AiAuditLogEntity MapToEntity(AiAuditLogEntry e) => new()
    {
        Id             = e.Id,
        Prompt         = e.Prompt,
        Response       = e.Response,
        ModelVersion   = e.ModelVersion,
        InputTokens    = e.InputTokens,
        OutputTokens   = e.OutputTokens,
        TotalTokens    = e.TotalTokens,
        LatencyMs      = e.LatencyMs,
        ConfidenceScore= e.ConfidenceScore,
        RequestType    = e.RequestType,
        PatientId      = e.PatientId,
        AbExperimentId = e.AbExperimentId,
        AbVariant      = e.AbVariant,
        UserId         = e.UserId,
        CreatedAt      = e.CreatedAt,
    };

    private static AiAuditLogEntry MapToDto(AiAuditLogEntity e) => new()
    {
        Id             = e.Id,
        Prompt         = e.Prompt,
        Response       = e.Response,
        ModelVersion   = e.ModelVersion,
        InputTokens    = e.InputTokens,
        OutputTokens   = e.OutputTokens,
        LatencyMs      = e.LatencyMs,
        ConfidenceScore= e.ConfidenceScore,
        RequestType    = e.RequestType,
        PatientId      = e.PatientId,
        AbExperimentId = e.AbExperimentId,
        AbVariant      = e.AbVariant,
        UserId         = e.UserId,
        CreatedAt      = e.CreatedAt,
    };
}
