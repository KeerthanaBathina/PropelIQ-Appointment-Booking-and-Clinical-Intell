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
/// Persists admin configuration changes to the <see cref="SystemConfig"/> key-value store
/// (and <see cref="ProviderAvailabilityTemplate"/> for slot templates).
///
/// Configuration is stored as JSON under namespaced keys:
///   admin.config.notifications       — NotificationTemplatesConfigDto
///   admin.config.hours               — BusinessHoursConfigDto
///   admin.config.risk_thresholds     — RiskThresholdsConfigDto
///
/// Slot templates read/write the normalised <see cref="ProviderAvailabilityTemplate"/> rows
/// rather than a JSON blob because provider slots are already a first-class entity.
///
/// Redis cache (5-min TTL, NFR-030):
///   Keys: admin:config:notifications, admin:config:hours, admin:config:risk_thresholds,
///         admin:config:slots
///
/// Every write appends an AuditLog entry (NFR-012, NFR-035).
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    // ── SystemConfig keys ─────────────────────────────────────────────────────
    private const string NotificationsKey   = "admin.config.notifications";
    private const string HoursKey           = "admin.config.hours";
    private const string RiskThresholdsKey  = "admin.config.risk_thresholds";

    // ── Cache keys ────────────────────────────────────────────────────────────
    private const string CacheNotifications  = "admin:config:notifications";
    private const string CacheHours          = "admin:config:hours";
    private const string CacheRisk           = "admin:config:risk_thresholds";
    private const string CacheSlots          = "admin:config:slots";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext          _db;
    private readonly ICacheService                 _cache;
    private readonly IAuditLogService              _audit;
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(
        ApplicationDbContext          db,
        ICacheService                 cache,
        IAuditLogService              audit,
        ILogger<ConfigurationService> logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slot templates
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SlotTemplatesConfigDto> GetSlotTemplatesAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<SlotTemplatesConfigDto>(CacheSlots, ct);
        if (cached is not null) return cached;

        // Build from ProviderAvailabilityTemplate rows: group by provider
        var rows = await _db.ProviderAvailabilityTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.ProviderId)
            .ThenBy(t => t.DayOfWeek)
            .ThenBy(t => t.StartTime)
            .ToListAsync(ct);

        var providers = rows
            .GroupBy(r => new { r.ProviderId, r.ProviderName })
            .Select(g =>
            {
                // Convert ProviderAvailabilityTemplate → SlotCellDto (5-slot grid: Mon–Fri, 9am–3pm)
                var slots = BuildSlotGrid(g.ToList());
                return new ProviderSlotTemplateDto(
                    g.Key.ProviderId,
                    g.Key.ProviderName,
                    slots);
            })
            .ToList();

        var result = new SlotTemplatesConfigDto(providers);
        await _cache.SetAsync(CacheSlots, result, CacheTtl, ct);
        return result;
    }

    /// <inheritdoc/>
    public async Task<SlotTemplatesConfigDto> UpdateSlotTemplatesAsync(
        SlotTemplatesConfigDto dto, Guid adminUserId, CancellationToken ct = default)
    {
        // For each provider update: upsert ProviderAvailabilityTemplate rows based on dto.
        // Cells marked IsAvailable=false are excluded (inactive templates).
        foreach (var provider in dto.Providers)
        {
            // Remove stale active templates for this provider
            var existing = await _db.ProviderAvailabilityTemplates
                .Where(t => t.ProviderId == provider.ProviderId)
                .ToListAsync(ct);

            _db.ProviderAvailabilityTemplates.RemoveRange(existing);

            // Insert new rows only for available cells
            foreach (var cell in provider.Slots.Where(s => s.Available))
            {
                if (!TryParseTime(cell.Time, out var startTime)) continue;

                _db.ProviderAvailabilityTemplates.Add(new ProviderAvailabilityTemplate
                {
                    ProviderId   = provider.ProviderId,
                    ProviderName = provider.ProviderName,
                    DayOfWeek    = cell.Day + 1, // 0=Mon stored as 1 (Monday in DayOfWeek enum)
                    StartTime    = startTime,
                    EndTime      = startTime.AddMinutes(60),
                    IsActive     = true,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        await _cache.RemoveAsync(CacheSlots, ct);

        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            "SlotTemplates",
            ipAddress: string.Empty,
            userAgent:  string.Empty,
            cancellationToken: ct);

        _logger.LogInformation(
            "Slot templates updated by admin {AdminId} — {Count} providers.",
            adminUserId, dto.Providers.Count);

        return await GetSlotTemplatesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Notification templates
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<NotificationTemplatesConfigDto> GetNotificationTemplatesAsync(
        CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<NotificationTemplatesConfigDto>(CacheNotifications, ct);
        if (cached is not null) return cached;

        var json = await GetConfigValueAsync(NotificationsKey, ct);
        var result = json is null
            ? new NotificationTemplatesConfigDto(DefaultNotificationTemplates())
            : JsonSerializer.Deserialize<NotificationTemplatesConfigDto>(json, JsonOpts)
              ?? new NotificationTemplatesConfigDto(DefaultNotificationTemplates());

        await _cache.SetAsync(CacheNotifications, result, CacheTtl, ct);
        return result;
    }

    /// <inheritdoc/>
    public async Task<NotificationTemplatesConfigDto> UpdateNotificationTemplateAsync(
        NotificationTemplateDto dto, Guid adminUserId, CancellationToken ct = default)
    {
        var current = await GetNotificationTemplatesAsync(ct);

        // Replace existing template with same ID, or add if new.
        var updated = current.Templates
            .Select(t => t.Id == dto.Id ? dto : t)
            .ToList();

        if (!updated.Any(t => t.Id == dto.Id))
            updated.Add(dto);

        var newConfig = new NotificationTemplatesConfigDto(updated);
        await PersistConfigAsync(NotificationsKey, newConfig, adminUserId, ct);
        await _cache.RemoveAsync(CacheNotifications, ct);

        _logger.LogInformation(
            "Notification template '{TemplateId}' updated by admin {AdminId}.", dto.Id, adminUserId);
        return newConfig;
    }

    /// <inheritdoc/>
    public async Task<NotificationTemplateDto> AddNotificationTemplateAsync(
        NotificationTemplateDto dto, Guid adminUserId, CancellationToken ct = default)
    {
        var current = await GetNotificationTemplatesAsync(ct);
        var newId   = Guid.NewGuid().ToString();
        var newItem = dto with { Id = newId };

        var updated = new NotificationTemplatesConfigDto(current.Templates.Append(newItem).ToList());
        await PersistConfigAsync(NotificationsKey, updated, adminUserId, ct);
        await _cache.RemoveAsync(CacheNotifications, ct);

        _logger.LogInformation(
            "Notification template '{Name}' added by admin {AdminId}.", dto.Name, adminUserId);
        return newItem;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Business hours
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<BusinessHoursConfigDto> GetBusinessHoursAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<BusinessHoursConfigDto>(CacheHours, ct);
        if (cached is not null) return cached;

        var json = await GetConfigValueAsync(HoursKey, ct);
        var result = json is null
            ? DefaultBusinessHours()
            : JsonSerializer.Deserialize<BusinessHoursConfigDto>(json, JsonOpts)
              ?? DefaultBusinessHours();

        await _cache.SetAsync(CacheHours, result, CacheTtl, ct);
        return result;
    }

    /// <inheritdoc/>
    public async Task<BusinessHoursConfigDto> UpdateBusinessHoursAsync(
        BusinessHoursConfigDto dto, Guid adminUserId, CancellationToken ct = default)
    {
        await PersistConfigAsync(HoursKey, dto, adminUserId, ct);
        await _cache.RemoveAsync(CacheHours, ct);

        _logger.LogInformation("Business hours updated by admin {AdminId}.", adminUserId);
        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Risk thresholds
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RiskThresholdsConfigDto> GetRiskThresholdsAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<RiskThresholdsConfigDto>(CacheRisk, ct);
        if (cached is not null) return cached;

        var json = await GetConfigValueAsync(RiskThresholdsKey, ct);
        var result = json is null
            ? new RiskThresholdsConfigDto(75, 45, 3, AutoOutreach: true)
            : JsonSerializer.Deserialize<RiskThresholdsConfigDto>(json, JsonOpts)
              ?? new RiskThresholdsConfigDto(75, 45, 3, AutoOutreach: true);

        await _cache.SetAsync(CacheRisk, result, CacheTtl, ct);
        return result;
    }

    /// <inheritdoc/>
    public async Task<RiskThresholdsConfigDto> UpdateRiskThresholdsAsync(
        RiskThresholdsConfigDto dto, Guid adminUserId, CancellationToken ct = default)
    {
        // Business rule: medium < high
        if (dto.MediumRiskThreshold >= dto.HighRiskThreshold)
            throw new ArgumentException(
                "MediumRiskThreshold must be less than HighRiskThreshold.");

        await PersistConfigAsync(RiskThresholdsKey, dto, adminUserId, ct);
        await _cache.RemoveAsync(CacheRisk, ct);

        _logger.LogInformation("Risk thresholds updated by admin {AdminId}.", adminUserId);
        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> GetConfigValueAsync(string key, CancellationToken ct)
    {
        var row = await _db.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == key, ct);
        return row?.ConfigValue;
    }

    private async Task PersistConfigAsync<T>(
        string key, T value, Guid adminUserId, CancellationToken ct) where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);

        var row = await _db.SystemConfigs
            .FirstOrDefaultAsync(c => c.ConfigKey == key, ct);

        if (row is null)
        {
            _db.SystemConfigs.Add(new SystemConfig
            {
                ConfigKey        = key,
                ConfigValue      = json,
                UpdatedByUserId  = adminUserId,
                UpdatedAt        = DateTime.UtcNow,
            });
        }
        else
        {
            row.ConfigValue     = json;
            row.UpdatedByUserId = adminUserId;
            row.UpdatedAt       = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            resourceType: $"SystemConfig:{key}",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            cancellationToken: ct);
    }

    /// <summary>
    /// Builds a 5-day × 5-time slot grid from flat ProviderAvailabilityTemplate rows.
    /// </summary>
    private static IReadOnlyList<SlotCellDto> BuildSlotGrid(
        IList<ProviderAvailabilityTemplate> rows)
    {
        var times = new[] { "9:00 AM", "10:00 AM", "11:00 AM", "2:00 PM", "3:00 PM" };
        var timeHours = new[] { 9, 10, 11, 14, 15 };
        var cells = new List<SlotCellDto>();

        for (var day = 0; day < 5; day++)
        {
            for (var ti = 0; ti < times.Length; ti++)
            {
                var dayOfWeek = day + 1; // 0-indexed Mon = DayOfWeek.Monday = 1
                var hour      = timeHours[ti];
                var available = rows.Any(r =>
                    r.DayOfWeek == dayOfWeek &&
                    r.StartTime.Hour == hour &&
                    r.IsActive);

                cells.Add(new SlotCellDto(day, times[ti], available));
            }
        }

        return cells;
    }

    private static bool TryParseTime(string timeLabel, out TimeOnly result)
    {
        if (TimeOnly.TryParse(timeLabel, out result)) return true;

        // Handle "9:00 AM" / "2:00 PM" style
        if (DateTime.TryParse(timeLabel, out var dt))
        {
            result = TimeOnly.FromDateTime(dt);
            return true;
        }

        result = default;
        return false;
    }

    private static IReadOnlyList<NotificationTemplateDto> DefaultNotificationTemplates() =>
    [
        new("1", "Appointment Reminder (24h)", "Email + SMS", "24h before appt",   "Active", null),
        new("2", "Appointment Confirmation",   "Email",       "On booking",         "Active", null),
        new("3", "No-Show Follow-up",          "SMS",         "15 min past appt",  "Active", null),
    ];

    private static BusinessHoursConfigDto DefaultBusinessHours() => new(
        RegularHours:
        [
            new("Monday–Friday", "8:00 AM", "5:00 PM"),
            new("Saturday",      "9:00 AM", "1:00 PM"),
            new("Sunday",        null,       null),
        ],
        Holidays: []);
}
