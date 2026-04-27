using Microsoft.Extensions.Diagnostics.HealthChecks;
using UPACIP.Service.Audit;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Health check for the Redis audit failover queue depth (US_064 edge case, NFR-032).
///
/// Status mapping:
/// <list type="table">
///   <listheader><term>Depth</term><description>Status</description></listheader>
///   <item><term>0</term><description><see cref="HealthStatus.Healthy"/> — no failover entries pending.</description></item>
///   <item><term>1 – 100</term><description><see cref="HealthStatus.Degraded"/> — DB may be recovering; entries are queued but will be flushed.</description></item>
///   <item><term>&gt; 100</term><description><see cref="HealthStatus.Unhealthy"/> — significant DB write failures; operational investigation required.</description></item>
///   <item><term>-1 (Redis unavailable)</term><description><see cref="HealthStatus.Degraded"/> — cannot determine depth; Redis may be down.</description></item>
/// </list>
///
/// Registered on the <c>/ready</c> health endpoint with tag <c>audit</c> so monitoring
/// dashboards can independently track audit-pipeline health without affecting overall
/// load-balancer readiness (tag "ready").
/// </summary>
public sealed class AuditQueueHealthCheck : IHealthCheck
{
    private readonly IAuditQueueService _queue;

    public AuditQueueHealthCheck(IAuditQueueService queue)
    {
        _queue = queue;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var depth = await _queue.GetQueueDepthAsync(cancellationToken);

        if (depth < 0)
        {
            // GetQueueDepthAsync returns -1 when Redis is unavailable.
            return HealthCheckResult.Degraded(
                $"Audit failover queue depth unavailable — Redis may be unreachable. " +
                $"Check Redis connectivity and audit pipeline health.");
        }

        if (depth == 0)
        {
            return HealthCheckResult.Healthy(
                "Audit failover queue is empty — all audit log entries written to PostgreSQL.");
        }

        if (depth <= 100)
        {
            return HealthCheckResult.Degraded(
                $"Audit failover queue depth: {depth}. " +
                $"PostgreSQL may be recovering; AuditQueueFlushWorker will flush entries automatically.");
        }

        return HealthCheckResult.Unhealthy(
            $"Audit failover queue depth: {depth}. " +
            $"Significant DB write failures detected. Investigate PostgreSQL connectivity and AuditQueueFlushWorker logs.");
    }
}
