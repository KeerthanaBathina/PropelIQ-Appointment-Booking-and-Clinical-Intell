using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.Service.Retention.Models;

namespace UPACIP.Service.Retention;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Guards against premature or unauthorised deletion/archival of data that must be
/// retained per HIPAA and internal policy (US_086, DR-016–DR-020, AC-1, AC-2).
///
/// Two responsibilities:
/// <list type="bullet">
///   <item>
///     <see cref="CanDeleteAsync"/> — returns <c>false</c> for audit logs (always) and
///     clinical records (when indefinite retention is configured), preventing any automated
///     process from touching these categories.
///   </item>
///   <item>
///     <see cref="IsProtectedByAuditLogAsync"/> — detects entities referenced by an audit
///     log entry within the 7-year retention window and blocks their removal regardless
///     of their own category policy (edge case 2).
///   </item>
/// </list>
/// </summary>
public interface IRetentionPolicyGuard
{
    /// <summary>
    /// Determines whether a record in <paramref name="category"/> created at
    /// <paramref name="recordCreatedAt"/> is eligible for automated deletion.
    /// </summary>
    /// <returns>
    /// <c>true</c> only when the record is outside its retention period and not
    /// otherwise protected. Always <c>false</c> for <see cref="RetentionCategory.AuditLogs"/>
    /// and <see cref="RetentionCategory.ClinicalRecords"/> (AC-1, AC-2).
    /// </returns>
    Task<bool> CanDeleteAsync(
        RetentionCategory category,
        DateTime          recordCreatedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> when any audit log entry references
    /// (<paramref name="resourceType"/>, <paramref name="resourceId"/>) within the
    /// configured audit-log retention window.
    ///
    /// A positive result means the entity must be retained for the full audit-log
    /// retention period even if its own category threshold has elapsed (edge case 2).
    /// </summary>
    Task<bool> IsProtectedByAuditLogAsync(
        string            resourceType,
        Guid              resourceId,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// EF Core-backed implementation of <see cref="IRetentionPolicyGuard"/>.
///
/// Scoped lifetime: injected per-request or per-scope by <c>DataRetentionService</c>
/// via <c>IServiceScopeFactory</c>.
/// </summary>
public sealed class RetentionPolicyGuard : IRetentionPolicyGuard
{
    private readonly IOptionsMonitor<RetentionPolicyOptions> _options;
    private readonly ApplicationDbContext                    _db;
    private readonly ILogger<RetentionPolicyGuard>          _logger;

    public RetentionPolicyGuard(
        IOptionsMonitor<RetentionPolicyOptions> options,
        ApplicationDbContext                    db,
        ILogger<RetentionPolicyGuard>           logger)
    {
        _options = options;
        _db      = db;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CanDeleteAsync(
        RetentionCategory category,
        DateTime          recordCreatedAt,
        CancellationToken ct = default)
    {
        var opts   = _options.CurrentValue;
        var now    = DateTime.UtcNow;
        bool allow = false;

        switch (category)
        {
            case RetentionCategory.AuditLogs:
                // AC-1: Audit logs are NEVER automatically deleted — not even after the 7-year
                // minimum has elapsed. Only a compliance-officer manual action (out of scope)
                // can remove them. Log a warning so the caller knows the guard blocked it.
                _logger.LogWarning(
                    "RETENTION_GUARD_BLOCKED: Attempted deletion of {Category} record " +
                    "created at {CreatedAt}. Audit logs are never auto-deleted (AC-1).",
                    category, recordCreatedAt);
                allow = false;
                break;

            case RetentionCategory.ClinicalRecords:
                // AC-2: Clinical records are indefinitely retained when the flag is set.
                if (opts.ClinicalRecordsIndefiniteRetention)
                {
                    _logger.LogWarning(
                        "RETENTION_GUARD_BLOCKED: Attempted deletion of {Category} record " +
                        "created at {CreatedAt}. ClinicalRecordsIndefiniteRetention=true (AC-2).",
                        category, recordCreatedAt);
                    allow = false;
                }
                else
                {
                    // Config explicitly allows deletion — honour it (edge case 1: future policy change).
                    allow = true;
                }
                break;

            case RetentionCategory.Notifications:
                // Eligible when older than the configured threshold.
                allow = recordCreatedAt < now.AddDays(-opts.NotificationLogRetentionDays);
                if (!allow)
                {
                    _logger.LogWarning(
                        "RETENTION_GUARD_BLOCKED: Attempted deletion of {Category} record " +
                        "created at {CreatedAt} — not yet past {Days}-day threshold.",
                        category, recordCreatedAt, opts.NotificationLogRetentionDays);
                }
                break;

            case RetentionCategory.Appointments:
                // Threshold check: appointment retention years per DR-018.
                // Archival is handled in task_002; this method just answers eligibility.
                allow = recordCreatedAt < now.AddYears(-opts.AppointmentRetentionYears);
                if (!allow)
                {
                    _logger.LogWarning(
                        "RETENTION_GUARD_BLOCKED: Attempted deletion of {Category} record " +
                        "created at {CreatedAt} — not yet past {Years}-year threshold.",
                        category, recordCreatedAt, opts.AppointmentRetentionYears);
                }
                break;

            case RetentionCategory.CancelledAppointments:
                allow = recordCreatedAt < now.AddYears(-opts.CancelledAppointmentRetentionYears);
                if (!allow)
                {
                    _logger.LogWarning(
                        "RETENTION_GUARD_BLOCKED: Attempted deletion of {Category} record " +
                        "created at {CreatedAt} — not yet past {Years}-year threshold.",
                        category, recordCreatedAt, opts.CancelledAppointmentRetentionYears);
                }
                break;

            default:
                // Unknown category — deny deletion as a safe default.
                _logger.LogWarning(
                    "RETENTION_GUARD_BLOCKED: Unknown retention category {Category}. " +
                    "Deletion denied by default.",
                    category);
                allow = false;
                break;
        }

        return Task.FromResult(allow);
    }

    /// <inheritdoc/>
    public async Task<bool> IsProtectedByAuditLogAsync(
        string            resourceType,
        Guid              resourceId,
        CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;

        if (!opts.EnforceAuditLogReferenceProtection)
            return false;

        var auditRetentionCutoff = DateTime.UtcNow.AddYears(-opts.AuditLogRetentionYears);

        // A resource is protected when at least one audit log entry references it
        // and that audit log is itself still within the 7-year retention window.
        var isProtected = await _db.AuditLogs
            .AsNoTracking()
            .AnyAsync(
                a => a.ResourceType == resourceType
                  && a.ResourceId   == resourceId
                  && a.Timestamp    > auditRetentionCutoff,
                ct);

        if (isProtected)
        {
            _logger.LogDebug(
                "RETENTION_GUARD_PROTECTED_BY_AUDIT: ResourceType={ResourceType} " +
                "ResourceId={ResourceId} is referenced by an audit log within the " +
                "{Years}-year retention window. Deletion blocked (edge case 2).",
                resourceType, resourceId, opts.AuditLogRetentionYears);
        }

        return isProtected;
    }
}
