using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Audit;

/// <summary>
/// Configuration settings for the audit log query subsystem (US_064 NFR-040, NFR-043).
/// Bound from the <c>AuditSettings</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class AuditSettings
{
    /// <summary>appsettings.json configuration section key.</summary>
    public const string SectionName = "AuditSettings";

    /// <summary>Maximum page size allowed for <c>GET /api/audit-logs</c> queries (NFR-040).</summary>
    [Range(1, 1000)]
    public int QueryMaxPageSize { get; init; } = 200;

    /// <summary>Default page size when the caller does not supply <c>pageSize</c>.</summary>
    [Range(1, 1000)]
    public int QueryDefaultPageSize { get; init; } = 50;

    /// <summary>
    /// Minimum retention period for audit log entries in years (HIPAA 45 CFR §164.312(b), NFR-043).
    /// Informational — actual partition pruning is handled by task_002_db migrations.
    /// </summary>
    [Range(1, 99)]
    public int RetentionYears { get; init; } = 7;
}
