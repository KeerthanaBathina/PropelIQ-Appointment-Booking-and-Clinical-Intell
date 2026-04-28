namespace UPACIP.Service.Monitoring;

/// <summary>
/// Abstraction that shields <see cref="UptimeMonitoringService"/> from a direct dependency on
/// ASP.NET Core's <c>HealthCheckService</c> (which is not available in a class-library project).
///
/// <para>
/// The API-layer implementation (<c>AspNetHealthStatusProvider</c>) delegates to the built-in
/// <c>HealthCheckService</c>, enabling direct in-process health evaluation without HTTP overhead.
/// </para>
/// </summary>
public interface IHealthStatusProvider
{
    /// <summary>
    /// Evaluates all registered health checks and returns the aggregate result and per-dependency statuses.
    /// </summary>
    /// <returns>
    /// A tuple of <c>IsHealthy</c> (<see langword="true"/> when the aggregate status is <c>Healthy</c>)
    /// and a dictionary mapping each dependency name to its status string
    /// (e.g. <c>{"database":"Healthy","redis":"Unhealthy"}</c>).
    /// </returns>
    Task<(bool IsHealthy, Dictionary<string, string> Statuses)> EvaluateAsync(CancellationToken ct = default);
}
