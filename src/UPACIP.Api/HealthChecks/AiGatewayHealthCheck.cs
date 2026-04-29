using Microsoft.Extensions.Diagnostics.HealthChecks;
using UPACIP.Service.AI;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// AI gateway dependency health check (US_099, AC-1, AC-3).
///
/// Delegates to <see cref="IAiHealthCheckService"/> — the existing Redis-cached AI availability
/// monitor (US_046). This avoids a direct HTTP call to the AI provider on every health probe,
/// which would consume tokens and add latency. Instead, the cached status (5-minute TTL) is
/// used:
/// <list type="bullet">
///   <item><c>IsAvailable = true</c> → <see cref="HealthCheckResult.Healthy"/>.</item>
///   <item><c>IsAvailable = false</c> → <see cref="HealthCheckResult.Degraded"/> — AI features
///         operate in manual-fallback mode; overall status is at most <c>Degraded</c>,
///         not <c>Unhealthy</c>, because the system can operate without AI (AC-3).</item>
/// </list>
///
/// A 400 ms per-check timeout is enforced by <see cref="HealthCheckConfiguration"/> to
/// ensure the /health response never exceeds 500 ms (edge case 1).
/// </summary>
public sealed class AiGatewayHealthCheck : IHealthCheck
{
    private readonly IAiHealthCheckService _aiHealthService;
    private readonly ILogger<AiGatewayHealthCheck> _logger;

    public AiGatewayHealthCheck(
        IAiHealthCheckService aiHealthService,
        ILogger<AiGatewayHealthCheck> logger)
    {
        _aiHealthService = aiHealthService;
        _logger          = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _aiHealthService.GetHealthStatusAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["provider"]    = "OpenAI",
                ["checkedAt"]   = status.CheckedAt.ToString("o"),
                ["isAvailable"] = status.IsAvailable,
            };

            if (!status.IsAvailable)
            {
                if (status.Reason is not null)
                    data["reason"] = status.Reason;

                return HealthCheckResult.Degraded(
                    "AI Gateway unavailable — AI features operating in manual-fallback mode",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "AI Gateway available",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HEALTH_CHECK_FAILED: AI Gateway health check exception");
            return HealthCheckResult.Degraded(
                "AI Gateway health check failed",
                exception: ex,
                data: new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }
}
