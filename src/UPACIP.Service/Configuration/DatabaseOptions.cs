namespace UPACIP.Service.Configuration;

/// <summary>
/// Strongly-typed options for database connection settings (US_101, AC-4).
/// Bound from the <c>Database</c> section in <c>appsettings.json</c>.
///
/// Connection strings and credentials MUST be supplied via environment variables
/// (<c>UPACIP_Database__ConnectionString</c>) or secret management — never committed
/// in plain text (OWASP A07).
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "Database";

    /// <summary>PostgreSQL connection string.  Provided via env var or user secrets.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Maximum number of pooled connections.  Default: 100.</summary>
    public int MaxPoolSize { get; init; } = 100;

    /// <summary>Per-command timeout in seconds.  Default: 30.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When <c>true</c>, EF Core logs parameter values.
    /// Must be <c>false</c> in Production to prevent PII leakage (OWASP A09).
    /// </summary>
    public bool EnableSensitiveDataLogging { get; init; }
}
