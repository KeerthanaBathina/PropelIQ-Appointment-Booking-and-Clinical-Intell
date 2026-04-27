using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Queue;

/// <summary>
/// Implements arrival queue business logic for US_052 (AC-1 through AC-4) and US_053 dashboard.
///
/// Architecture (TR-009):
///   QueueController → QueueService → ApplicationDbContext (direct, no separate repository
///   layer needed — thin CRUD without complex business rules warranting a repo abstraction).
///
/// Caching strategy (NFR-030, NFR-004):
///   GET /queue/today (no filters) — cached under <c>queue:today:{date:yyyyMMdd}</c>, TTL 5 min.
///   GET /queue/today (with filters, US_053) — per-filter granular keys via IQueueCacheService.
///   Any mutation (arrive, cancel, override, no-show) invalidates both the per-date key and
///   all per-filter variants for today.
///
/// Concurrency (TR-015):
///   QueueEntry.Version is used as an EF Core concurrency token. On DbUpdateConcurrencyException
///   the service refreshes the entity and retries once before propagating a Conflict result.
///
/// Audit logging (TR-028):
///   Every status mutation appends an AuditLog entry via IAuditLogService with the staff
///   user ID from the JWT claim and the X-Correlation-ID request header.
/// </summary>
public sealed class QueueService : IQueueService
{
    // Redis cache key format — invalidated on every mutation
    private const string CacheKeyFormat = "queue:today:{0:yyyyMMdd}";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Redis key for admin-configured threshold (60-second TTL allows fast propagation, AC-3)
    private const string ThresholdCacheKey = "queue:config:wait_threshold_minutes";
    private static readonly TimeSpan ThresholdCacheTtl = TimeSpan.FromSeconds(60);

    // No-show detection threshold (AC-2)
    internal static readonly TimeSpan NoShowThreshold = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext     _db;
    private readonly ICacheService            _cache;
    private readonly IQueueCacheService       _queueCache;
    private readonly IAuditLogService         _auditLog;
    private readonly ILogger<QueueService>    _logger;
    private readonly int                      _waitThresholdMinutes;

    public QueueService(
        ApplicationDbContext    db,
        ICacheService           cache,
        IQueueCacheService      queueCache,
        IAuditLogService        auditLog,
        IOptions<QueueSettings> queueSettings,
        ILogger<QueueService>   logger)
    {
        _db                   = db;
        _cache                = cache;
        _queueCache           = queueCache;
        _auditLog             = auditLog;
        _logger               = logger;
        _waitThresholdMinutes = queueSettings.Value.WaitTimeThresholdMinutes;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /queue/today (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueTodayResponse> GetTodayQueueAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey();

        // Cache-aside: return cached response immediately when Redis is warm
        var cached = await _cache.GetAsync<QueueTodayResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("QueueService.GetTodayQueue: cache hit, key={Key}.", cacheKey);
            return cached;
        }

        var response = await FetchFromDatabaseAsync(cancellationToken);

        // Write-through — failures are swallowed inside ICacheService
        await _cache.SetAsync(cacheKey, response, CacheTtl, cancellationToken);

        return response;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /queue/today?provider=&status=&page= (US_053, AC-1, AC-4, EC-1)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueuePagedResponseDto> GetTodayQueuePagedAsync(
        QueueFilterParams filters,
        CancellationToken cancellationToken = default)
    {
        // 1. Try granular per-filter cache (IQueueCacheService)
        var cached = await _queueCache.GetCachedQueueAsync(filters, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug(
                "QueueService.GetTodayQueuePaged: cache hit for provider={Provider}, status={Status}, page={Page}.",
                filters.Provider, filters.Status, filters.Page);
            return cached;
        }

        // 2. Fetch full today's list (priority sort baked in) — reuses the existing projection
        var todayResponse = await FetchFromDatabaseAsync(cancellationToken);
        var now           = DateTime.UtcNow;

        // 3. Apply filters (AND logic — all supplied filters must match, US_056 AC-1)
        IEnumerable<QueueEntryDto> filtered = todayResponse.Data;

        if (filters.HasProviderFilter)
        {
            filtered = filtered.Where(e =>
                string.Equals(e.ProviderName, filters.Provider, StringComparison.OrdinalIgnoreCase));
        }

        if (filters.HasAppointmentTypeFilter)
        {
            filtered = filtered.Where(e =>
                string.Equals(e.AppointmentType, filters.AppointmentType, StringComparison.OrdinalIgnoreCase));
        }

        if (filters.HasStatusFilter)
        {
            filtered = filtered.Where(e =>
                string.Equals(e.Status, filters.Status, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        var totalCount   = filteredList.Count;

        // 4. Paginate (sort is already applied by FetchFromDatabaseAsync — priority desc, appt time asc)
        var pageSize = Math.Clamp(filters.PageSize, 1, 100);    // guard against malformed input
        var page     = Math.Max(filters.Page, 1);
        var pageData = filteredList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // 5. Compute aggregate stats across all filtered entries (not just the page slice)
        var waitingEntries = filteredList
            .Where(e => e.Status is QueueStatusStrings.Waiting or QueueStatusStrings.ArrivedLate)
            .ToList();

        int? avgWait = waitingEntries.Count > 0
            ? (int)Math.Round(waitingEntries.Average(e => e.WaitTimeMinutes))
            : null;

        int thresholdAlertCount = waitingEntries.Count(e => e.WaitTimeMinutes > _waitThresholdMinutes);

        var result = new QueuePagedResponseDto
        {
            Data                   = pageData,
            TotalCount             = totalCount,
            Page                   = page,
            PageSize               = pageSize,
            AverageWaitTimeMinutes = avgWait,
            LastUpdated            = now,
            ThresholdAlertCount    = thresholdAlertCount,
        };

        // 6. Cache the per-filter result (failures swallowed inside IQueueCacheService)
        await _queueCache.SetCachedQueueAsync(filters, result, cancellationToken);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /queue/arrive (AC-1)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueServiceResult<QueueEntryDto>> MarkArrivalAsync(
        Guid appointmentId,
        Guid staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Load appointment with navigation (OWASP A01: never trust client-supplied patient data)
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.QueueEntry)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
            return QueueServiceResult<QueueEntryDto>.NotFound(
                $"Appointment {appointmentId} not found.");

        // Duplicate arrival prevention — 409 Conflict (task checklist item 6)
        if (appointment.QueueEntry is not null &&
            appointment.QueueEntry.Status is QueueStatus.Waiting or QueueStatus.ArrivedLate)
        {
            var existingArrival = appointment.QueueEntry.ArrivalTimestamp;
            return QueueServiceResult<QueueEntryDto>.Conflict(
                $"Patient already marked as arrived at {existingArrival:g}");
        }

        var now = DateTime.UtcNow;

        // Detect whether this arrival is a staff override of an auto no-show (US_055 edge case 2).
        // A no-show override occurs when:
        //   • The appointment was already auto-marked NoShow by NoShowDetectionService, OR
        //   • The QueueEntry status is NoShow.
        // In both cases we reset back to Scheduled/Waiting and clear the auto-no-show flag so the
        // detection service skips the appointment on the next 60-second cycle.
        var isNoShowOverride = appointment.Status == AppointmentStatus.NoShow ||
                               (appointment.QueueEntry is not null &&
                                appointment.QueueEntry.Status == QueueStatus.NoShow);

        QueueEntry entry;
        if (appointment.QueueEntry is not null)
        {
            // Update existing entry (normal re-arrival or staff override of auto no-show)
            entry = appointment.QueueEntry;
            entry.Status           = QueueStatus.Waiting;
            entry.ArrivalTimestamp = now;
            entry.WaitTimeMinutes  = 0;
            entry.UpdatedAt        = now;
            entry.Version++;                // advance concurrency token (TR-015)

            // Override path: clear auto-detection flags so detection service skips on next cycle
            if (isNoShowOverride)
            {
                entry.IsAutoNoShow       = false;
                entry.IsDelayedDetection = false;
            }
        }
        else
        {
            entry = new QueueEntry
            {
                Id               = Guid.NewGuid(),
                AppointmentId    = appointmentId,
                ArrivalTimestamp = now,
                WaitTimeMinutes  = 0,
                Priority         = QueuePriority.Normal,
                Status           = QueueStatus.Waiting,
                CreatedAt        = now,
                UpdatedAt        = now,
            };
            _db.QueueEntries.Add(entry);
        }

        // Restore appointment status on no-show override; otherwise leave as-is.
        if (isNoShowOverride)
            appointment.Status = AppointmentStatus.Scheduled;

        appointment.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        await _auditLog.LogAsync(
            isNoShowOverride ? AuditAction.NoShowOverridden : AuditAction.ArrivalMarked,
            staffUserId,
            "QueueEntry",
            ipAddress: string.Empty,
            userAgent:  string.Empty,
            resourceId: entry.Id,
            cancellationToken);

        _logger.LogInformation(
            "QueueService.MarkArrival: appointmentId={AppointmentId}, queueId={QueueId}, " +
            "staffUserId={StaffUserId}, correlationId={CorrelationId}, noShowOverride={IsNoShowOverride}.",
            appointmentId, entry.Id, staffUserId, correlationId, isNoShowOverride);

        var dto = ToDto(entry, appointment, queuePosition: 1); // position recalculated on next GET
        return QueueServiceResult<QueueEntryDto>.Ok(dto);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /queue/{queueId}/status (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueServiceResult<QueueEntryDto>> UpdateStatusAsync(
        Guid queueId,
        string newStatusStr,
        Guid staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _db.QueueEntries
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .FirstOrDefaultAsync(q => q.Id == queueId, cancellationToken);

        if (entry is null)
            return QueueServiceResult<QueueEntryDto>.NotFound(
                $"Queue entry {queueId} not found.");

        if (!TryParseQueueStatus(newStatusStr, out var newStatus))
            return QueueServiceResult<QueueEntryDto>.Unprocessable(
                $"'{newStatusStr}' is not a valid queue status.");

        var now     = DateTime.UtcNow;
        var oldStatus = entry.Status;
        entry.Status    = newStatus;
        entry.UpdatedAt = now;
        entry.Version++;                    // advance concurrency token (TR-015)
        entry.Version++;                   // advance concurrency token (TR-015)

        if (newStatus == QueueStatus.Cancelled)
        {
            entry.CancelledAt            = now;
            entry.Appointment.Status     = AppointmentStatus.Cancelled;
            entry.Appointment.UpdatedAt  = now;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Optimistic concurrency conflict (TR-015): refresh and signal the caller
            _logger.LogWarning(
                "QueueService.UpdateStatus: concurrency conflict on queueId={QueueId}.", queueId);
            await _db.Entry(entry).ReloadAsync(cancellationToken);
            return QueueServiceResult<QueueEntryDto>.Conflict(
                "Queue entry was modified by another request. Please retry.");
        }

        await InvalidateCacheAsync(cancellationToken);

        var auditAction = newStatus == QueueStatus.Cancelled
            ? AuditAction.ArrivalCancelled
            : AuditAction.DataModify;

        await _auditLog.LogAsync(auditAction, staffUserId, "QueueEntry",
            ipAddress: string.Empty, userAgent: string.Empty,
            resourceId: entry.Id, cancellationToken);

        _logger.LogInformation(
            "QueueService.UpdateStatus: queueId={QueueId}, {OldStatus}→{NewStatus}, " +
            "staffUserId={StaffUserId}, correlationId={CorrelationId}.",
            queueId, oldStatus, newStatus, staffUserId, correlationId);

        var dto = ToDto(entry, entry.Appointment, queuePosition: 0);
        return QueueServiceResult<QueueEntryDto>.Ok(dto);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /queue/{queueId}/override (edge case)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueServiceResult<QueueEntryDto>> OverrideNoShowAsync(
        Guid queueId,
        string reason,
        Guid staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _db.QueueEntries
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .FirstOrDefaultAsync(q => q.Id == queueId, cancellationToken);

        if (entry is null)
            return QueueServiceResult<QueueEntryDto>.NotFound(
                $"Queue entry {queueId} not found.");

        // Guard: only no-show entries can be overridden (422)
        if (entry.Appointment.Status != AppointmentStatus.NoShow &&
            entry.Status             != QueueStatus.NoShow)
        {
            return QueueServiceResult<QueueEntryDto>.Unprocessable(
                "Override is only valid for appointments with no-show status.");
        }

        var now = DateTime.UtcNow;

        entry.Status              = QueueStatus.ArrivedLate;
        entry.ArrivalTimestamp    = now;
        entry.WaitTimeMinutes     = 0;
        entry.OverrideReason      = reason;
        entry.OverriddenByUserId  = staffUserId;
        entry.UpdatedAt           = now;
        entry.Version++;                    // advance concurrency token (TR-015)
        entry.Version++;                   // advance concurrency token (TR-015)

        entry.Appointment.Status    = AppointmentStatus.Scheduled; // re-open
        entry.Appointment.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.NoShowOverridden,
            staffUserId,
            "QueueEntry",
            ipAddress: string.Empty,
            userAgent:  string.Empty,
            resourceId: entry.Id,
            cancellationToken);

        _logger.LogInformation(
            "QueueService.OverrideNoShow: queueId={QueueId}, reason={Reason}, " +
            "staffUserId={StaffUserId}, correlationId={CorrelationId}.",
            queueId, reason, staffUserId, correlationId);

        var dto = ToDto(entry, entry.Appointment, queuePosition: 0);
        return QueueServiceResult<QueueEntryDto>.Ok(dto);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_055 — Wait Threshold Config (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> GetWaitThresholdAsync(CancellationToken cancellationToken = default)
    {
        // Redis cache-aside — fast path for polling frontend (30-second interval)
        var cached = await _cache.GetAsync<ThresholdWrapper>(ThresholdCacheKey, cancellationToken);
        if (cached is not null)
            return cached.Value;

        // Fall back to appsettings default (task_003 can add DB persistence later)
        return _waitThresholdMinutes;
    }

    public async Task UpdateWaitThresholdAsync(
        int   thresholdMinutes,
        Guid  adminUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Write to Redis — all active polling will pick up the new value within 60 s (AC-3)
        await _cache.SetAsync(
            ThresholdCacheKey,
            new ThresholdWrapper(thresholdMinutes),
            ThresholdCacheTtl,
            cancellationToken);

        // Invalidate queue cache so ExceedsWaitThreshold / WaitAlertLevel recompute immediately
        await InvalidateCacheAsync(cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.WaitThresholdConfigChanged,
            adminUserId,
            "QueueConfig",
            ipAddress: string.Empty,
            userAgent:  string.Empty,
            resourceId: null,
            cancellationToken);

        _logger.LogInformation(
            "QueueService.UpdateWaitThreshold: {Minutes} min, adminUserId={AdminUserId}, correlationId={CorrelationId}.",
            thresholdMinutes, adminUserId, correlationId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Background: mark no-shows (AC-1)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task MarkNoShowsAsync(bool isDelayedDetection = false, CancellationToken cancellationToken = default)
    {
        var cutoff     = DateTime.UtcNow.Subtract(NoShowThreshold);
        var todayStart = DateTime.UtcNow.Date;

        // Find scheduled appointments where no active queue entry exists AND
        // the appointment time has passed the 15-minute threshold
        var overdueAppointments = await _db.Appointments
            .Where(a =>
                a.Status          == AppointmentStatus.Scheduled           &&
                a.AppointmentTime >= todayStart                            &&
                a.AppointmentTime <= cutoff                                &&
                (a.QueueEntry == null ||
                 (a.QueueEntry.Status != QueueStatus.Waiting &&
                  a.QueueEntry.Status != QueueStatus.InVisit  &&
                  a.QueueEntry.Status != QueueStatus.ArrivedLate)))
            .Include(a => a.QueueEntry)
            .ToListAsync(cancellationToken);

        if (overdueAppointments.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var appt in overdueAppointments)
        {
            appt.Status    = AppointmentStatus.NoShow;
            appt.UpdatedAt = now;

            if (appt.QueueEntry is not null)
            {
                appt.QueueEntry.Status              = QueueStatus.NoShow;
                appt.QueueEntry.IsAutoNoShow        = true;
                appt.QueueEntry.IsDelayedDetection  = isDelayedDetection;
                appt.QueueEntry.UpdatedAt           = now;
                appt.QueueEntry.Version++; // advance concurrency token (TR-015)
                appt.QueueEntry.Version++;  // advance concurrency token (TR-015)
            }
            else
            {
                // Create a placeholder queue entry so the no-show is visible in the queue
                var entry = new QueueEntry
                {
                    Id                 = Guid.NewGuid(),
                    AppointmentId      = appt.Id,
                    ArrivalTimestamp   = appt.AppointmentTime, // use appointment time as proxy
                    WaitTimeMinutes    = (int)(now - appt.AppointmentTime).TotalMinutes,
                    Priority           = QueuePriority.Normal,
                    Status             = QueueStatus.NoShow,
                    IsAutoNoShow       = true,
                    IsDelayedDetection = isDelayedDetection,
                    CreatedAt          = now,
                    UpdatedAt          = now,
                };
                _db.QueueEntries.Add(entry);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation(
            "QueueService.MarkNoShows: {Count} appointments marked as no-show.",
            overdueAppointments.Count);

        // Append audit entries for each no-show (system actor — no staff user ID)
        foreach (var appt in overdueAppointments)
        {
            await _auditLog.LogAsync(
                AuditAction.NoShowAutoDetected,
                userId:       null,   // system-generated — no staff user
                resourceType: "Appointment",
                ipAddress:    string.Empty,
                userAgent:    "NoShowDetectionService",
                resourceId:   appt.Id,
                cancellationToken,
                systemEvent:  true);  // override null-userId guard (US_055 AC-4)
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /queue/{queueId}/priority (US_054 AC-1, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueServiceResult<QueueReorderResponse>> SetPriorityAsync(
        Guid   queueId,
        string priority,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Validate priority string
        if (!TryParseQueuePriority(priority, out var newPriority))
            return QueueServiceResult<QueueReorderResponse>.Unprocessable(
                $"'{priority}' is not a valid priority. Use 'urgent' or 'normal'.");

        var entry = await _db.QueueEntries
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .FirstOrDefaultAsync(q => q.Id == queueId, cancellationToken);

        if (entry is null)
            return QueueServiceResult<QueueReorderResponse>.NotFound(
                $"Queue entry {queueId} not found.");

        var now             = DateTime.UtcNow;
        var oldPriority     = entry.Priority;
        var oldPosition     = entry.QueuePosition;
        var oldPriorityStr  = QueueStatusStrings.PriorityFromEnum(oldPriority);

        // No-op if priority unchanged
        if (oldPriority == newPriority)
        {
            var noOpResponse = await BuildQueueResponseAsync(cancellationToken);
            return QueueServiceResult<QueueReorderResponse>.Ok(noOpResponse);
        }

        entry.Priority  = newPriority;
        entry.UpdatedAt = now;
        entry.Version++;

        // Recalculate all queue_position values (urgent tier first, normal tier second)
        var todayEntries = await LoadTodayActiveEntriesForPositioningAsync(cancellationToken);
        RecalculateQueuePositions(todayEntries, entry);

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        // Append queue-specific audit record (AC-3)
        _db.QueueAuditLogs.Add(new QueueAuditLog
        {
            QueueId          = queueId,
            ActionType       = "PRIORITY_CHANGE",
            StaffUserId      = staffUserId == Guid.Empty ? null : staffUserId,
            OriginalPosition = oldPosition,
            NewPosition      = entry.QueuePosition,
            OriginalPriority = oldPriorityStr,
            NewPriority      = QueueStatusStrings.PriorityFromEnum(newPriority),
            Timestamp        = now,
        });

        // Also write to the general audit log (TR-028)
        await _auditLog.LogAsync(
            AuditAction.QueuePriorityChanged,
            staffUserId == Guid.Empty ? null : staffUserId,
            "QueueEntry",
            ipAddress: string.Empty,
            userAgent:  string.Empty,
            resourceId: entry.Id,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);  // persist QueueAuditLog

        _logger.LogInformation(
            "QueueService.SetPriority: queueId={QueueId}, {Old}→{New}, " +
            "staffUserId={StaffUserId}, correlationId={CorrelationId}.",
            queueId, oldPriorityStr, priority, staffUserId, correlationId);

        var response = await BuildQueueResponseAsync(cancellationToken);
        return QueueServiceResult<QueueReorderResponse>.Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /queue/reorder (US_054 AC-2, AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueServiceResult<QueueReorderResponse>> ReorderQueueAsync(
        Guid   queueId,
        int    newPosition,
        Guid   staffUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _db.QueueEntries
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .FirstOrDefaultAsync(q => q.Id == queueId, cancellationToken);

        if (entry is null)
            return QueueServiceResult<QueueReorderResponse>.NotFound(
                $"Queue entry {queueId} not found.");

        var now         = DateTime.UtcNow;
        var oldPosition = entry.QueuePosition;

        // Load today's active entries to determine valid position range
        var todayEntries = await LoadTodayActiveEntriesForPositioningAsync(cancellationToken);
        var clampedPosition = Math.Clamp(newPosition, 1, Math.Max(1, todayEntries.Count));

        if (oldPosition == clampedPosition)
        {
            var noOpResponse = await BuildQueueResponseAsync(cancellationToken);
            return QueueServiceResult<QueueReorderResponse>.Ok(noOpResponse);
        }

        // Shift other entries' positions to make room for the moved entry
        if (clampedPosition < oldPosition)
        {
            // Moving up — increment positions of entries in [clampedPosition, oldPosition)
            foreach (var other in todayEntries
                .Where(e => e.Id != queueId &&
                            e.QueuePosition >= clampedPosition &&
                            e.QueuePosition <  oldPosition))
            {
                other.QueuePosition++;
                other.UpdatedAt = now;
                other.Version++;
            }
        }
        else
        {
            // Moving down — decrement positions of entries in (oldPosition, clampedPosition]
            foreach (var other in todayEntries
                .Where(e => e.Id != queueId &&
                            e.QueuePosition >  oldPosition &&
                            e.QueuePosition <= clampedPosition))
            {
                other.QueuePosition--;
                other.UpdatedAt = now;
                other.Version++;
            }
        }

        entry.QueuePosition = clampedPosition;
        entry.UpdatedAt     = now;
        entry.Version++;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning(
                "QueueService.ReorderQueue: concurrency conflict on queueId={QueueId}.", queueId);

            // Reload all affected entries and return current state as 409 payload
            foreach (var e in todayEntries) await _db.Entry(e).ReloadAsync(cancellationToken);
            var conflictResponse = await BuildQueueResponseAsync(cancellationToken);
            return QueueServiceResult<QueueReorderResponse>.Conflict(
                "Queue was updated by another staff member. Refreshing...");
        }

        await InvalidateCacheAsync(cancellationToken);

        // Append queue-specific audit record (AC-3)
        var priorityStr = QueueStatusStrings.PriorityFromEnum(entry.Priority);
        _db.QueueAuditLogs.Add(new QueueAuditLog
        {
            QueueId          = queueId,
            ActionType       = "REORDER",
            StaffUserId      = staffUserId == Guid.Empty ? null : staffUserId,
            OriginalPosition = oldPosition,
            NewPosition      = clampedPosition,
            OriginalPriority = priorityStr,
            NewPriority      = priorityStr,  // priority unchanged during manual reorder
            Timestamp        = now,
        });

        await _auditLog.LogAsync(
            AuditAction.QueueReordered,
            staffUserId == Guid.Empty ? null : staffUserId,
            "QueueEntry",
            ipAddress: string.Empty,
            userAgent:  string.Empty,
            resourceId: entry.Id,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);  // persist QueueAuditLog

        _logger.LogInformation(
            "QueueService.ReorderQueue: queueId={QueueId}, position {Old}→{New}, " +
            "staffUserId={StaffUserId}, correlationId={CorrelationId}.",
            queueId, oldPosition, clampedPosition, staffUserId, correlationId);

        var response = await BuildQueueResponseAsync(cancellationToken);
        return QueueServiceResult<QueueReorderResponse>.Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<QueueTodayResponse> FetchFromDatabaseAsync(CancellationToken cancellationToken)
    {
        var now        = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);

        // Resolve dynamic threshold (Redis-cached, falls back to appsettings default)
        var threshold = await GetWaitThresholdAsync(cancellationToken);

        var entries = await _db.QueueEntries
            .AsNoTracking()
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .Where(q =>
                q.Appointment.AppointmentTime >= todayStart &&
                q.Appointment.AppointmentTime <  todayEnd   &&
                q.Appointment.Patient.DeletedAt == null)
            .OrderByDescending(q => q.Priority == QueuePriority.Urgent)
            .ThenBy(q => q.Appointment.AppointmentTime)
            .ToListAsync(cancellationToken);

        var dtos = entries.Select((q, idx) =>
        {
            var waitMinutes = q.ArrivalTimestamp == default
                ? 0
                : (int)(now - q.ArrivalTimestamp).TotalMinutes;
            var wait = Math.Max(0, waitMinutes);

            return new QueueEntryDto
            {
                QueueId              = q.Id,
                AppointmentId        = q.AppointmentId,
                PatientName          = q.Appointment.Patient.FullName,
                AppointmentTime      = q.Appointment.AppointmentTime,
                ArrivalTimestamp     = q.ArrivalTimestamp == default ? null : q.ArrivalTimestamp,
                WaitTimeMinutes      = wait,
                Priority             = QueueStatusStrings.PriorityFromEnum(q.Priority),
                Status               = QueueStatusStrings.FromEnum(q.Status),
                AppointmentStatus    = QueueStatusStrings.AppointmentStatusFromEnum(q.Appointment.Status),
                QueuePosition        = idx + 1,
                ProviderName         = q.Appointment.ProviderName,
                AppointmentType      = q.Appointment.AppointmentType,
                IsAutoNoShow         = q.IsAutoNoShow,
                IsDelayedDetection   = q.IsDelayedDetection,
                ExceedsWaitThreshold = wait >= threshold,
                WaitAlertLevel       = ComputeWaitAlertLevel(wait, threshold),
                WaitThresholdMinutes = threshold,
            };
        }).ToList();

        return new QueueTodayResponse { Data = dtos, TotalCount = dtos.Count };
    }

    private static QueueEntryDto ToDto(QueueEntry entry, Appointment appt, int queuePosition,
        int threshold = 30)
    {
        var now = DateTime.UtcNow;
        var waitMinutes = entry.ArrivalTimestamp == default
            ? 0
            : (int)(now - entry.ArrivalTimestamp).TotalMinutes;
        var wait = Math.Max(0, waitMinutes);

        return new QueueEntryDto
        {
            QueueId              = entry.Id,
            AppointmentId        = entry.AppointmentId,
            PatientName          = appt.Patient?.FullName ?? string.Empty,
            AppointmentTime      = appt.AppointmentTime,
            ArrivalTimestamp     = entry.ArrivalTimestamp == default ? null : entry.ArrivalTimestamp,
            WaitTimeMinutes      = wait,
            Priority             = QueueStatusStrings.PriorityFromEnum(entry.Priority),
            Status               = QueueStatusStrings.FromEnum(entry.Status),
            AppointmentStatus    = QueueStatusStrings.AppointmentStatusFromEnum(appt.Status),
            QueuePosition        = queuePosition,
            ProviderName         = appt.ProviderName,
            AppointmentType      = appt.AppointmentType,
            IsAutoNoShow         = entry.IsAutoNoShow,
            IsDelayedDetection   = entry.IsDelayedDetection,
            ExceedsWaitThreshold = wait >= threshold,
            WaitAlertLevel       = ComputeWaitAlertLevel(wait, threshold),
            WaitThresholdMinutes = threshold,
        };
    }

    /// <summary>
    /// Computes the wait alert severity level (US_055 AC-2).
    /// </summary>
    private static string ComputeWaitAlertLevel(int waitMinutes, int thresholdMinutes)
    {
        if (waitMinutes >= thresholdMinutes * 1.5) return "critical";
        if (waitMinutes >= thresholdMinutes)        return "warning";
        return "none";
    }

    /// <summary>Thin wrapper to let <see cref="ICacheService"/> serialise a plain int.</summary>
    private sealed record ThresholdWrapper(int Value);

    private string BuildCacheKey()
        => string.Format(CacheKeyFormat, DateTime.UtcNow);

    private async Task InvalidateCacheAsync(CancellationToken cancellationToken)
    {
        // Invalidate both the legacy per-date key (US_052) and all per-filter variants (US_053)
        await _cache.RemoveAsync(BuildCacheKey(), cancellationToken);
        await _queueCache.InvalidateQueueCacheAsync(cancellationToken);
    }

    private static bool TryParseQueueStatus(string value, out QueueStatus result)
    {
        result = value.ToLowerInvariant() switch
        {
            "waiting"      => QueueStatus.Waiting,
            "in_visit"     => QueueStatus.InVisit,
            "completed"    => QueueStatus.Completed,
            "no_show"      => QueueStatus.NoShow,
            "arrived_late" => QueueStatus.ArrivedLate,
            "cancelled"    => QueueStatus.Cancelled,
            _              => (QueueStatus)(-1),
        };
        return (int)result >= 0;
    }

    private static bool TryParseQueuePriority(string value, out QueuePriority result)
    {
        result = value.ToLowerInvariant() switch
        {
            "urgent" => QueuePriority.Urgent,
            "normal" => QueuePriority.Normal,
            _        => (QueuePriority)(-1),
        };
        return (int)result >= 0;
    }

    /// <summary>
    /// Loads today's active (non-terminal) queue entries tracked by EF Core,
    /// ordered by current QueuePosition, for use in position recalculation and reorder operations.
    /// Includes navigation properties needed by BuildQueueResponseAsync.
    /// </summary>
    private async Task<List<QueueEntry>> LoadTodayActiveEntriesForPositioningAsync(
        CancellationToken cancellationToken)
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd   = todayStart.AddDays(1);

        return await _db.QueueEntries
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .Where(q =>
                q.Appointment.AppointmentTime >= todayStart &&
                q.Appointment.AppointmentTime <  todayEnd   &&
                q.Appointment.Patient.DeletedAt == null      &&
                q.Status != QueueStatus.Completed           &&
                q.Status != QueueStatus.Cancelled           &&
                q.Status != QueueStatus.NoShow)
            .OrderBy(q => q.QueuePosition)
            .ThenBy(q => q.ArrivalTimestamp)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recalculates QueuePosition values for all entries in <paramref name="todayEntries"/>
    /// using the two-tier priority sort (US_054 AC-1, AC-4):
    ///   Tier 1 — Urgent entries ordered by ArrivalTimestamp ASC → positions 1…n
    ///   Tier 2 — Normal entries ordered by ArrivalTimestamp ASC → positions n+1…m
    ///
    /// The modified <paramref name="targetEntry"/> (already with its updated Priority) must be
    /// included in <paramref name="todayEntries"/> or passed separately so its position is set.
    /// </summary>
    private static void RecalculateQueuePositions(
        List<QueueEntry> todayEntries,
        QueueEntry       targetEntry)
    {
        // Merge target into todayEntries view if not already present (handles fresh loads)
        var allEntries = todayEntries.Any(e => e.Id == targetEntry.Id)
            ? todayEntries
            : [.. todayEntries, targetEntry];

        var urgentEntries = allEntries
            .Where(e => e.Priority == QueuePriority.Urgent)
            .OrderBy(e => e.ArrivalTimestamp)
            .ThenBy(e => e.Id)          // deterministic tiebreaker
            .ToList();

        var normalEntries = allEntries
            .Where(e => e.Priority != QueuePriority.Urgent)
            .OrderBy(e => e.ArrivalTimestamp)
            .ThenBy(e => e.Id)
            .ToList();

        var position = 1;
        foreach (var e in urgentEntries) e.QueuePosition = position++;
        foreach (var e in normalEntries) e.QueuePosition = position++;
    }

    /// <summary>
    /// Builds a <see cref="QueueReorderResponse"/> from a fresh database read.
    /// Used as the return value of SetPriorityAsync and ReorderQueueAsync.
    /// </summary>
    private async Task<QueueReorderResponse> BuildQueueResponseAsync(
        CancellationToken cancellationToken)
    {
        var todayResponse = await FetchFromDatabaseAsync(cancellationToken);
        return new QueueReorderResponse
        {
            Data       = todayResponse.Data,
            TotalCount = todayResponse.TotalCount,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_056 — Queue History (AC-3, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<QueueHistoryResponse?> GetQueueHistoryAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseHistoryDates(startDate, endDate, out var startUtc, out var endUtc))
            return null;

        var cacheKey = $"queue:history:{startDate}:{endDate}";
        var cached   = await _cache.GetAsync<QueueHistoryResponse>(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;

        var endExclusive = endUtc.AddDays(1);   // range is inclusive on both ends

        var rows = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.Appointment.AppointmentTime >= startUtc &&
                q.Appointment.AppointmentTime <  endExclusive)
            .GroupBy(q => q.Appointment.AppointmentTime.Date)
            .Select(g => new
            {
                Date              = g.Key,
                TotalEntries      = g.Count(),
                NoShowCount       = g.Count(q => q.Status == QueueStatus.NoShow),
                PatientThroughput = g.Count(q => q.Status == QueueStatus.Completed),
                AvgWait           = g.Average(q => (double?)q.WaitTimeMinutes),
            })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        var metrics = rows.Select(r => new QueueDailyMetrics
        {
            Date              = r.Date.ToString("yyyy-MM-dd"),
            AvgWaitTimeMinutes = r.AvgWait.HasValue ? (int)Math.Round(r.AvgWait.Value) : null,
            NoShowCount        = r.NoShowCount,
            PatientThroughput  = r.PatientThroughput,
            TotalEntries       = r.TotalEntries,
        }).ToList();

        // Aggregate totals across the full range
        var allAvgWait = rows.Any(r => r.AvgWait.HasValue)
            ? (int?)Math.Round(rows.Where(r => r.AvgWait.HasValue).Average(r => r.AvgWait!.Value))
            : null;

        var summary = new QueueHistorySummary
        {
            AvgWaitTimeMinutes = allAvgWait,
            NoShowCount        = rows.Sum(r => r.NoShowCount),
            PatientThroughput  = rows.Sum(r => r.PatientThroughput),
            TotalEntries       = rows.Sum(r => r.TotalEntries),
        };

        // Earliest available date for frontend "suggest a start date" UX
        var earliestDate = await _db.QueueEntries
            .AsNoTracking()
            .OrderBy(q => q.Appointment.AppointmentTime)
            .Select(q => (DateTime?)q.Appointment.AppointmentTime)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new QueueHistoryResponse
        {
            StartDate         = startDate,
            EndDate           = endDate,
            Metrics           = metrics,
            Summary           = summary,
            AvailableFromDate = earliestDate.HasValue
                ? earliestDate.Value.ToString("yyyy-MM-dd")
                : null,
        };

        await _cache.SetAsync(cacheKey, response, CacheTtl, cancellationToken);
        return response;
    }

    public async Task<byte[]?> ExportQueueHistoryAsCsvAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken = default)
    {
        var historyResponse = await GetQueueHistoryAsync(startDate, endDate, cancellationToken);
        if (historyResponse is null)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,TotalEntries,PatientThroughput,NoShowCount,AvgWaitTimeMinutes");

        foreach (var row in historyResponse.Metrics)
        {
            sb.Append(row.Date).Append(',')
              .Append(row.TotalEntries).Append(',')
              .Append(row.PatientThroughput).Append(',')
              .Append(row.NoShowCount).Append(',')
              .AppendLine(row.AvgWaitTimeMinutes.HasValue
                  ? row.AvgWaitTimeMinutes.Value.ToString()
                  : string.Empty);
        }

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Parses and validates the history date range. Returns <c>false</c> when:
    ///   • Either date is not a valid yyyy-MM-dd string
    ///   • startDate is after endDate
    ///   • The range exceeds 365 days
    /// </summary>
    private static bool TryParseHistoryDates(
        string startDate, string endDate,
        out DateTime startUtc, out DateTime endUtc)
    {
        startUtc = default;
        endUtc   = default;

        if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var start))
            return false;

        if (!DateTime.TryParseExact(endDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var end))
            return false;

        if (start > end)
            return false;

        if ((end - start).TotalDays > 365)
            return false;

        startUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        endUtc   = DateTime.SpecifyKind(end,   DateTimeKind.Utc);
        return true;
    }
}