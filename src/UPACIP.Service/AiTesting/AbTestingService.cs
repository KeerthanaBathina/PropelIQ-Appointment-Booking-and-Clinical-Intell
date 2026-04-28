using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.AiTesting.Models;

namespace UPACIP.Service.AiTesting;

/// <summary>
/// Scoped A/B testing service backed by PostgreSQL (via EF Core) and Redis
/// (active-experiment cache) (US_080 task_001, AC-1, AC-2, AIR-O10).
///
/// <para>
/// <b>Variant assignment algorithm</b>: SHA-256(<c>experimentId + userId</c>) →
/// first 4 bytes as uint32 → <c>percentage = hash % 100</c>.
/// If <c>percentage &lt; TrafficSplitPercentage</c> → Candidate; else → Control.
/// Purely computational — no Redis/DB state required for assignment.
/// </para>
///
/// <para>
/// <b>Active experiment cache</b>: <c>ab:active-experiment</c> in Redis with 60 s TTL.
/// Cache is cleared immediately on terminate/pause/create to ensure consistent routing.
/// </para>
///
/// <para>Scoped lifetime — depends on scoped <see cref="ApplicationDbContext"/>.</para>
/// </summary>
public sealed class AbTestingService : IAbTestingService
{
    private const string ActiveExperimentCacheKey = "ab:active-experiment";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
    };

    private readonly ApplicationDbContext             _db;
    private readonly IDistributedCache                _cache;
    private readonly IOptions<AiCostRatesOptions>     _costRates;
    private readonly ILogger<AbTestingService>        _logger;

    public AbTestingService(
        ApplicationDbContext          db,
        IDistributedCache             cache,
        IOptions<AiCostRatesOptions>  costRates,
        ILogger<AbTestingService>     logger)
    {
        _db        = db;
        _cache     = cache;
        _costRates = costRates;
        _logger    = logger;
    }

    // ── IAbTestingService ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AbExperiment> CreateExperimentAsync(AbExperiment experiment, CancellationToken ct = default)
    {
        // Pause any currently active experiment — only one can be Active at a time.
        var existing = await _db.AbExperiments
            .Where(e => e.Status == "Active")
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.Status    = "Paused";
            existing.UpdatedAt = DateTime.UtcNow;
        }

        var entity = new AbExperimentEntity
        {
            Id                    = experiment.Id == Guid.Empty ? Guid.NewGuid() : experiment.Id,
            ControlModelId        = experiment.ControlModelId,
            CandidateModelId      = experiment.CandidateModelId,
            TrafficSplitPercentage = experiment.TrafficSplitPercentage,
            Status                = "Active",
            StartDate             = DateTime.UtcNow,
            EndDate               = experiment.EndDate,
            Description           = experiment.Description,
            CreatedByUserId       = experiment.CreatedByUserId,
            CreatedAt             = DateTime.UtcNow,
            UpdatedAt             = DateTime.UtcNow,
        };

        _db.AbExperiments.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Invalidate cache so the next request sees the new experiment immediately.
        await InvalidateCacheAsync();

        _logger.LogInformation(
            "AbTestingService: experiment created. " +
            "ExperimentId={Id} ControlModel={Control} CandidateModel={Candidate} " +
            "TrafficSplit={Split}% CreatedBy={AdminId}",
            entity.Id, entity.ControlModelId, entity.CandidateModelId,
            entity.TrafficSplitPercentage, entity.CreatedByUserId);

        return MapToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<AbExperiment?> GetActiveExperimentAsync(CancellationToken ct = default)
    {
        // 1. Try Redis cache first.
        try
        {
            var cached = await _cache.GetStringAsync(ActiveExperimentCacheKey, ct);
            if (cached is not null)
            {
                var dto = JsonSerializer.Deserialize<AbExperiment>(cached, JsonOptions);
                if (dto is not null)
                    return dto;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AbTestingService: Redis cache miss on GetActiveExperimentAsync; falling back to DB.");
        }

        // 2. Query DB.
        var entity = await _db.AbExperiments
            .AsNoTracking()
            .Where(e => e.Status == "Active")
            .FirstOrDefaultAsync(ct);

        if (entity is null)
            return null;

        var result = MapToDto(entity);

        // 3. Populate cache.
        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await _cache.SetStringAsync(ActiveExperimentCacheKey, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AbTestingService: failed to cache active experiment.");
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<AbVariant> AssignVariantAsync(Guid experimentId, string userId, CancellationToken ct = default)
    {
        // Deterministic assignment: SHA-256(experimentId + userId) → uint32 → mod 100.
        // No DB or Redis state required — same inputs always produce the same output.
        var input    = $"{experimentId}:{userId}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hashUint  = BitConverter.ToUInt32(hashBytes, 0);
        var percentage = (int)(hashUint % 100);

        // We need the experiment's TrafficSplitPercentage.
        // The middleware calls GetActiveExperimentAsync first so the experiment object is
        // available — but this interface takes only the ID to remain pure.
        // Re-fetch from DB (or use EF identity map if same scope).
        return AssignVariantInternalAsync(experimentId, percentage, ct);
    }

    private async Task<AbVariant> AssignVariantInternalAsync(
        Guid experimentId, int hashedPercentage, CancellationToken ct)
    {
        var entity = await _db.AbExperiments
            .AsNoTracking()
            .Where(e => e.Id == experimentId)
            .Select(e => new { e.TrafficSplitPercentage, e.Status })
            .FirstOrDefaultAsync(ct);

        if (entity is null || entity.Status != "Active")
            return AbVariant.Control;

        return hashedPercentage < entity.TrafficSplitPercentage
            ? AbVariant.Candidate
            : AbVariant.Control;
    }

    /// <inheritdoc/>
    public async Task RecordMetricAsync(AbMetricRecord metric, CancellationToken ct = default)
    {
        var entity = new AbMetricRecordEntity
        {
            Id           = metric.Id == Guid.Empty ? Guid.NewGuid() : metric.Id,
            ExperimentId = metric.ExperimentId,
            Variant      = metric.Variant.ToString(),
            Accuracy     = metric.Accuracy,
            LatencyMs    = metric.LatencyMs,
            TokensUsed   = metric.TokensUsed,
            EstimatedCost = metric.EstimatedCost,
            RequestType  = metric.RequestType,
            CreatedAt    = DateTime.UtcNow,
        };

        _db.AbMetricRecords.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task TerminateExperimentAsync(Guid experimentId, CancellationToken ct = default)
    {
        var entity = await _db.AbExperiments
            .Where(e => e.Id == experimentId)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
        {
            _logger.LogWarning("AbTestingService: TerminateExperiment called on unknown id {Id}.", experimentId);
            return;
        }

        entity.Status    = "Terminated";
        entity.EndDate   = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync();

        _logger.LogInformation(
            "AbTestingService: experiment terminated. ExperimentId={Id} — all traffic reverted to control model.",
            experimentId);
    }

    /// <inheritdoc/>
    public async Task PauseExperimentAsync(Guid experimentId, CancellationToken ct = default)
    {
        var entity = await _db.AbExperiments
            .Where(e => e.Id == experimentId)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
        {
            _logger.LogWarning("AbTestingService: PauseExperiment called on unknown id {Id}.", experimentId);
            return;
        }

        entity.Status    = "Paused";
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync();

        _logger.LogInformation(
            "AbTestingService: experiment paused. ExperimentId={Id}.", experimentId);
    }

    /// <inheritdoc/>
    public async Task<AbExperimentResult> GetExperimentResultsAsync(
        Guid experimentId, CancellationToken ct = default)
    {
        var metrics = await _db.AbMetricRecords
            .AsNoTracking()
            .Where(m => m.ExperimentId == experimentId)
            .ToListAsync(ct);

        var controlRows   = metrics.Where(m => m.Variant == "Control").ToList();
        var candidateRows = metrics.Where(m => m.Variant == "Candidate").ToList();

        var controlMetrics   = AggregateVariant(controlRows);
        var candidateMetrics = AggregateVariant(candidateRows);

        var isSignificant = IsStatisticallySignificant(
            controlRows.Count,
            candidateRows.Count,
            controlMetrics.MeanAccuracy,
            candidateMetrics.MeanAccuracy,
            controlRows.Where(m => m.Accuracy.HasValue).Select(m => m.Accuracy!.Value).ToList(),
            candidateRows.Where(m => m.Accuracy.HasValue).Select(m => m.Accuracy!.Value).ToList());

        return new AbExperimentResult
        {
            ExperimentId        = experimentId,
            ControlMetrics      = controlMetrics,
            CandidateMetrics    = candidateMetrics,
            ControlSampleSize   = controlRows.Count,
            CandidateSampleSize = candidateRows.Count,
            IsStatisticallySignificant = isSignificant,
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AbExperiment>> ListExperimentsAsync(
        string?           statusFilter = null,
        int               page         = 1,
        int               pageSize     = 20,
        CancellationToken ct           = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.AbExperiments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter))
            query = query.Where(e => e.Status == statusFilter);

        var rows = await query
            .OrderByDescending(e => e.StartDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows.Select(MapToDto).ToList();
    }

    // ── Public helper for deterministic assignment ────────────────────────────

    /// <summary>
    /// Computes the variant assignment for a (experimentId, userId) pair using the
    /// deterministic SHA-256 hash algorithm — exposed as a static method for testing.
    /// </summary>
    public static AbVariant ComputeVariant(Guid experimentId, string userId, int trafficSplitPercentage)
    {
        var input     = $"{experimentId}:{userId}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hashUint  = BitConverter.ToUInt32(hashBytes, 0);
        var percentage = (int)(hashUint % 100);
        return percentage < trafficSplitPercentage ? AbVariant.Candidate : AbVariant.Control;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AbVariantMetrics AggregateVariant(List<AbMetricRecordEntity> rows)
    {
        if (rows.Count == 0)
            return new AbVariantMetrics();

        var accuracies = rows
            .Where(m => m.Accuracy.HasValue)
            .Select(m => (double)m.Accuracy!.Value)
            .ToList();

        var meanAccuracy = accuracies.Count > 0
            ? (float)(accuracies.Average())
            : 0f;

        var latencies = rows.Select(m => (double)m.LatencyMs).OrderBy(v => v).ToList();
        var medianLatency = latencies.Count > 0
            ? (float)latencies[latencies.Count / 2]
            : 0f;

        var p95Latency = latencies.Count > 0
            ? (float)latencies[(int)Math.Floor(latencies.Count * 0.95)]
            : 0f;

        var totalCost   = rows.Sum(m => m.EstimatedCost);
        var avgCost     = rows.Count > 0 ? totalCost / rows.Count : 0m;

        return new AbVariantMetrics
        {
            MeanAccuracy          = meanAccuracy,
            MedianLatencyMs       = medianLatency,
            P95LatencyMs          = p95Latency,
            TotalCost             = totalCost,
            AverageCostPerRequest = avgCost,
        };
    }

    /// <summary>
    /// Simplified z-test for two-proportion accuracy difference.
    /// Requires minimum 30 samples per variant and p &lt; 0.05 to declare significance.
    /// </summary>
    private static bool IsStatisticallySignificant(
        int controlCount, int candidateCount,
        float controlMean, float candidateMean,
        List<float> controlAccuracies, List<float> candidateAccuracies)
    {
        if (controlCount < 30 || candidateCount < 30)
            return false;

        if (controlAccuracies.Count < 5 || candidateAccuracies.Count < 5)
            return false;

        var pooledProportion = (controlMean * controlCount + candidateMean * candidateCount)
                               / (controlCount + candidateCount);

        var se = Math.Sqrt(
            pooledProportion * (1 - pooledProportion) * (1.0 / controlCount + 1.0 / candidateCount));

        if (se < 1e-10)
            return false;

        var z = Math.Abs((candidateMean - controlMean) / se);

        // |z| > 1.96 corresponds to p < 0.05 (two-tailed).
        return z > 1.96;
    }

    private async Task InvalidateCacheAsync()
    {
        try
        {
            await _cache.RemoveAsync(ActiveExperimentCacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AbTestingService: failed to invalidate active-experiment cache.");
        }
    }

    private static AbExperiment MapToDto(AbExperimentEntity e) => new()
    {
        Id                    = e.Id,
        ControlModelId        = e.ControlModelId,
        CandidateModelId      = e.CandidateModelId,
        TrafficSplitPercentage = e.TrafficSplitPercentage,
        Status                = Enum.TryParse<AbExperimentStatus>(e.Status, out var status)
                                    ? status
                                    : AbExperimentStatus.Active,
        StartDate             = e.StartDate,
        EndDate               = e.EndDate,
        Description           = e.Description,
        CreatedByUserId       = e.CreatedByUserId,
    };
}

// ── Cost rate options ─────────────────────────────────────────────────────────

/// <summary>Per-model cost rates bound from the <c>AiCostRates</c> config section.</summary>
public sealed class AiCostRatesOptions
{
    public const string SectionName = "AiCostRates";

    /// <summary>Cost per 1,000 input tokens (USD).</summary>
    public decimal CostPer1kInputTokens { get; init; } = 0.00015m;

    /// <summary>Cost per 1,000 output tokens (USD).</summary>
    public decimal CostPer1kOutputTokens { get; init; } = 0.0006m;
}
