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
/// Provides per-template GET and PUT operations for the notification template
/// configuration store (US_060 AC-1, AC-2).
///
/// <para>
/// Templates are persisted as a JSON blob in the <see cref="SystemConfig"/> table
/// under the key <c>admin.config.notifications</c> — the same key used by
/// <see cref="ConfigurationService"/> for list operations.  This service reads and
/// writes that key so that GET /api/admin/config/notifications (list) always reflects
/// the latest state.
/// </para>
///
/// Redis cache key: <c>admin:config:notifications</c> (5-min TTL, invalidated on write).
///
/// Every write appends an <see cref="AuditLog"/> entry with admin attribution (AC-4).
/// </summary>
public sealed class NotificationTemplateService : INotificationTemplateService
{
    // Shared with ConfigurationService so both read/write the same JSON blob.
    private const string ConfigKey  = "admin.config.notifications";
    private const string CacheKey   = "admin:config:notifications";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext                _db;
    private readonly ICacheService                       _cache;
    private readonly IAuditLogService                    _audit;
    private readonly ILogger<NotificationTemplateService> _logger;

    public NotificationTemplateService(
        ApplicationDbContext                  db,
        ICacheService                         cache,
        IAuditLogService                      audit,
        ILogger<NotificationTemplateService>  logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INotificationTemplateService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<NotificationTemplateDto?> GetByIdAsync(
        string id, CancellationToken ct = default)
    {
        var all = await LoadAllAsync(ct);
        return all.FirstOrDefault(t => t.Id == id);
    }

    /// <inheritdoc/>
    public async Task<NotificationTemplateDto> UpdateByIdAsync(
        string                                id,
        UpdateNotificationTemplateByIdRequest request,
        Guid                                  adminUserId,
        CancellationToken                     ct = default)
    {
        var templates = (await LoadAllAsync(ct)).ToList();

        var existing = templates.FirstOrDefault(t => t.Id == id)
            ?? throw new KeyNotFoundException($"Notification template '{id}' was not found.");

        // Capture previous values for structured audit log.
        var previousJson = JsonSerializer.Serialize(existing, JsonOpts);

        var updated = existing with
        {
            Channel      = request.Channel,
            Subject      = request.Subject,
            BodyTemplate = request.BodyTemplate,
            Status       = request.Status,
            UpdatedAt    = DateTime.UtcNow,
            UpdatedBy    = adminUserId.ToString(),
        };

        var newTemplates = templates
            .Select(t => t.Id == id ? updated : t)
            .ToList();

        await PersistAsync(newTemplates, adminUserId, ct);

        _logger.LogInformation(
            "Notification template {TemplateId} updated by admin {AdminId}. " +
            "Previous={Previous} New={New} CorrelationId={CorrelationId}",
            id, adminUserId, previousJson,
            JsonSerializer.Serialize(updated, JsonOpts),
            Guid.NewGuid()); // correlation id placeholder — real one from HttpContext in controller

        return updated;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<NotificationTemplateDto>> LoadAllAsync(
        CancellationToken ct)
    {
        // Check cache first.
        var cached = await _cache.GetAsync<NotificationTemplatesConfigDto>(CacheKey, ct);
        if (cached is not null) return cached.Templates;

        var row = await _db.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);

        if (row?.ConfigValue is null)
            return DefaultTemplates();

        var config = JsonSerializer.Deserialize<NotificationTemplatesConfigDto>(
                         row.ConfigValue, JsonOpts)
                     ?? new NotificationTemplatesConfigDto(DefaultTemplates());

        // Warm cache.
        await _cache.SetAsync(CacheKey, config, CacheTtl, ct);
        return config.Templates;
    }

    private async Task PersistAsync(
        IReadOnlyList<NotificationTemplateDto> templates,
        Guid                                   adminUserId,
        CancellationToken                      ct)
    {
        var config = new NotificationTemplatesConfigDto(templates);
        var json   = JsonSerializer.Serialize(config, JsonOpts);

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

        // Invalidate cache so next GET returns fresh data.
        await _cache.RemoveAsync(CacheKey, ct);

        // Append immutable audit trail entry (NFR-012, NFR-035, AC-4).
        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            resourceType: "NotificationTemplate",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            cancellationToken: ct);
    }

    private static IReadOnlyList<NotificationTemplateDto> DefaultTemplates() =>
    [
        new("1", "Appointment Reminder (24h)", "Email + SMS", "24h before appt",  "Active",
            BodyTemplate: "Hi {{patient_name}}, your appointment is on {{date}} at {{time}} with {{provider}}."),
        new("2", "Appointment Confirmation",   "Email",       "On booking",        "Active",
            BodyTemplate: "Hello {{patient_name}}, your appointment on {{date}} at {{time}} with {{provider}} has been confirmed."),
        new("3", "No-Show Follow-up",          "SMS",         "15 min past appt", "Active",
            BodyTemplate: "Hi {{patient_name}}, we missed you for your {{date}} appointment. Please reschedule at your earliest convenience."),
    ];
}
