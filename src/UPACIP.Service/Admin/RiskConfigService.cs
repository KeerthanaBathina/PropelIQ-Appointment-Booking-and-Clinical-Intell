using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Admin;

/// <summary>
/// Manages the risk configuration record including scoring parameter weights and the
/// deferred recalculation flag (US_060 AC-3, AC-4).
///
/// <para>
/// Stored in <see cref="SystemConfig"/> under the key
/// <c>admin.config.risk</c> as a JSON-serialised <see cref="RiskConfigDto"/>.
/// On first access the service auto-creates the record seeded from
/// <see cref="ConfigurationService"/>'s <c>admin.config.risk_thresholds</c> key
/// (or uses sensible defaults if that key is also absent).
/// </para>
///
/// Redis cache key: <c>admin:config:risk</c> (5-min TTL, invalidated on write).
///
/// Every write appends an <see cref="AuditLog"/> entry with admin attribution (AC-4).
/// </summary>
public sealed class RiskConfigService : IRiskConfigService
{
    private const string ConfigKey       = "admin.config.risk";
    private const string LegacyConfigKey = "admin.config.risk_thresholds";
    private const string CacheKey        = "admin:config:risk";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext      _db;
    private readonly ICacheService             _cache;
    private readonly IAuditLogService          _audit;
    private readonly ILogger<RiskConfigService> _logger;

    public RiskConfigService(
        ApplicationDbContext        db,
        ICacheService               cache,
        IAuditLogService            audit,
        ILogger<RiskConfigService>  logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IRiskConfigService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RiskConfigDto> GetAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<RiskConfigDto>(CacheKey, ct);
        if (cached is not null) return cached;

        var result = await LoadFromDbAsync(ct);

        await _cache.SetAsync(CacheKey, result, CacheTtl, ct);
        return result;
    }

    /// <inheritdoc/>
    public async Task<RiskConfigDto> UpdateAsync(
        UpdateRiskConfigRequest request,
        Guid                    adminUserId,
        CancellationToken       ct = default)
    {
        var previous     = await LoadFromDbAsync(ct);
        var previousJson = JsonSerializer.Serialize(previous, JsonOpts);

        var updated = new RiskConfigDto(
            HighRiskThreshold:         request.HighRiskThreshold,
            MediumRiskThreshold:       request.MediumRiskThreshold,
            MinAppointmentsForAiScore: request.MinAppointmentsForAiScore,
            AutoOutreach:              request.AutoOutreach,
            ScoringParameters:         request.ScoringParameters,
            // Signal background batch job to recalculate risk scores (edge case).
            RecalculationPending:      true,
            UpdatedAt:                 DateTime.UtcNow,
            UpdatedBy:                 adminUserId.ToString());

        await PersistAsync(updated, adminUserId, ct);

        // Structured audit log capturing before/after values (AC-4, NFR-012).
        _logger.LogInformation(
            "Risk configuration updated by admin {AdminId}. " +
            "Previous={Previous} New={New}",
            adminUserId,
            previousJson,
            JsonSerializer.Serialize(updated, JsonOpts));

        return updated;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<RiskConfigDto> LoadFromDbAsync(CancellationToken ct)
    {
        // Try the US_060 key first.
        var row = await _db.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);

        if (row?.ConfigValue is not null)
        {
            var stored = JsonSerializer.Deserialize<RiskConfigDto>(row.ConfigValue, JsonOpts);
            if (stored is not null) return stored;
        }

        // Fall back to legacy key written by ConfigurationService (US_058).
        var legacyRow = await _db.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == LegacyConfigKey, ct);

        if (legacyRow?.ConfigValue is not null)
        {
            var legacy = JsonSerializer.Deserialize<RiskThresholdsConfigDto>(
                             legacyRow.ConfigValue, JsonOpts);
            if (legacy is not null)
                return MapFromLegacy(legacy);
        }

        return DefaultConfig();
    }

    private async Task PersistAsync(
        RiskConfigDto     config,
        Guid              adminUserId,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(config, JsonOpts);

        var row = await _db.SystemConfigs
            .FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);

        if (row is null)
        {
            _db.SystemConfigs.Add(new SystemConfig
            {
                ConfigKey       = ConfigKey,
                ConfigValue     = json,
                UpdatedByUserId = adminUserId,
                UpdatedAt       = DateTime.UtcNow,
            });
        }
        else
        {
            row.ConfigValue     = json;
            row.UpdatedByUserId = adminUserId;
            row.UpdatedAt       = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await _cache.RemoveAsync(CacheKey, ct);

        // Append immutable audit trail (NFR-012, NFR-035, AC-4).
        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            resourceType: "RiskConfig",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            cancellationToken: ct);
    }

    // ── Defaults / mapping ────────────────────────────────────────────────────

    private static RiskConfigDto DefaultConfig() => new(
        HighRiskThreshold:         75,
        MediumRiskThreshold:       45,
        MinAppointmentsForAiScore: 3,
        AutoOutreach:              true,
        ScoringParameters:         new ScoringParametersDto(
            PriorNoShowsWeight:        0.50m,
            CancellationHistoryWeight: 0.30m,
            AppointmentLeadTimeWeight: 0.20m),
        RecalculationPending: false,
        UpdatedAt:            null,
        UpdatedBy:            null);

    private static RiskConfigDto MapFromLegacy(RiskThresholdsConfigDto legacy) => new(
        HighRiskThreshold:         legacy.HighRiskThreshold,
        MediumRiskThreshold:       legacy.MediumRiskThreshold,
        MinAppointmentsForAiScore: legacy.MinAppointmentsForAiScore,
        AutoOutreach:              legacy.AutoOutreach,
        ScoringParameters:         new ScoringParametersDto(
            PriorNoShowsWeight:        0.50m,
            CancellationHistoryWeight: 0.30m,
            AppointmentLeadTimeWeight: 0.20m),
        RecalculationPending: false,
        UpdatedAt:            null,
        UpdatedBy:            null);
}
