using Asp.Versioning;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Semantic versioning constants and policy documentation for the UPACIP API (US_102, AC-2).
///
/// The API versioning services (<c>AddApiVersioning</c> / <c>AddApiExplorer</c>) are registered
/// in <c>SwaggerConfiguration.AddSwaggerDocumentation</c> as part of the Swashbuckle setup
/// established in US_098/task_002.  This class documents the versioning policy and provides
/// compile-time constants consumed by <see cref="VersionDeprecationMiddleware"/> and
/// <see cref="UPACIP.Api.Swagger.DeprecatedVersionDocumentFilter"/>.
///
/// Versioning strategy (TR-035):
/// <list type="bullet">
///   <item>
///     URL segment versioning — <c>/api/v{major}/...</c>.
///     Selected over header-based versioning for discoverability and HTTP cacheability.
///   </item>
///   <item>
///     <c>AssumeDefaultVersionWhenUnspecified = true</c> — version-unaware clients are
///     routed to <see cref="CurrentVersion"/> without receiving a 400 error (edge case 2).
///   </item>
///   <item>
///     <c>ReportApiVersions = true</c> — responses include
///     <c>api-supported-versions</c> and <c>api-deprecated-versions</c> headers.
///   </item>
/// </list>
///
/// Semantic versioning rules (SemVer 2.0.0):
/// <list type="bullet">
///   <item>
///     <b>MAJOR</b> (1.0 → 2.0): Breaking changes — removed/renamed fields, changed response
///     shapes, removed endpoints.  Previous version marked <c>Deprecated = true</c> on
///     <c>[ApiVersion]</c> and remains accessible for
///     <see cref="MinDeprecationPeriodMonths"/> months.
///   </item>
///   <item>
///     <b>MINOR</b> (1.0 → 1.1): Additive changes — new fields, new endpoints, new optional
///     parameters.  Backward compatible; no action required by consumers.
///   </item>
///   <item>
///     <b>PATCH</b>: Bug fixes, documentation updates; no observable behavioral change.
///   </item>
/// </list>
/// </summary>
public static class ApiVersioningConfiguration
{
    /// <summary>The current stable API version.  Default version for version-unaware clients.</summary>
    public static readonly ApiVersion CurrentVersion = new(1, 0);

    /// <summary>
    /// Minimum calendar months between a version deprecation announcement and its sunset date.
    /// Ensures consumers have adequate time to migrate (TR-035).
    /// </summary>
    public const int MinDeprecationPeriodMonths = 6;

    /// <summary>
    /// Versioning policy identifier — used in documentation and migration guides.
    /// </summary>
    public const string VersioningPolicy = "SemVer 2.0.0 — URL segment versioning";
}
