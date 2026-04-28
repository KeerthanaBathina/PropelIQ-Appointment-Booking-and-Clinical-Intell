namespace UPACIP.Service.Infrastructure.Models;

/// <summary>
/// Classifies an API endpoint for circuit-breaker policy selection
/// (US_082 task_001, edge case 2 — traffic spike handling).
///
/// <para>
/// Route classification rules (case-insensitive prefix matching):
/// <list type="table">
///   <item><b>Critical</b> — <c>/api/auth/*</c>, <c>/api/appointments/book*</c>,
///     <c>/health</c>, <c>/ready</c>. Never circuit-broken.</item>
///   <item><b>NonCritical</b> — <c>/api/admin/*</c>, <c>/api/dashboard/*</c>,
///     <c>/api/reports/*</c>, <c>/api/history/*</c>. Circuit-broken at the lower threshold.</item>
///   <item><b>Standard</b> — all other <c>/api/*</c> routes. Circuit-broken at the higher threshold.</item>
/// </list>
/// </para>
/// </summary>
public enum EndpointClassification
{
    /// <summary>
    /// High-importance operations that must never be circuit-broken.
    /// Includes authentication, appointment booking, and health probes.
    /// </summary>
    Critical = 0,

    /// <summary>
    /// Normal operational endpoints circuit-broken only under sustained failure
    /// (higher threshold).  Includes document management and medical coding APIs.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Low-priority read-heavy endpoints that are circuit-broken quickly to shed load
    /// from non-essential operations during traffic spikes. Includes admin, dashboards,
    /// reports, and history views.
    /// </summary>
    NonCritical = 2,
}
