using UPACIP.Service.AiTesting.Models;

namespace UPACIP.Service.AiTesting;

/// <summary>
/// A/B testing framework for AI model version comparison (US_080 task_001, AC-1, AC-2, AIR-O10).
///
/// <para>
/// Provides deterministic variant assignment, metric recording, experiment lifecycle
/// management, and results aggregation for comparing two AI model versions.
/// </para>
/// </summary>
public interface IAbTestingService
{
    /// <summary>
    /// Creates and activates a new A/B experiment.
    /// Any currently Active experiment is paused first (only one can be Active at a time).
    /// </summary>
    /// <param name="experiment">Experiment definition to persist.</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    /// <returns>The created experiment with its assigned <c>Id</c>.</returns>
    Task<AbExperiment> CreateExperimentAsync(AbExperiment experiment, CancellationToken ct = default);

    /// <summary>
    /// Returns the currently active experiment, or <see langword="null"/> if none is running.
    /// Results are cached in Redis (60 s TTL) to avoid per-request DB queries.
    /// </summary>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    Task<AbExperiment?> GetActiveExperimentAsync(CancellationToken ct = default);

    /// <summary>
    /// Deterministically assigns a variant to a user for the specified experiment.
    /// The same <paramref name="userId"/> always receives the same variant within one experiment
    /// (SHA-256 hash of <c>experimentId + userId</c>, mod 100 compared to
    /// <c>TrafficSplitPercentage</c>).
    /// </summary>
    /// <param name="experimentId">Experiment to assign the user to.</param>
    /// <param name="userId">Authenticated user identifier.</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    /// <returns><see cref="AbVariant.Candidate"/> or <see cref="AbVariant.Control"/>.</returns>
    Task<AbVariant> AssignVariantAsync(Guid experimentId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Records a per-request performance metric data point for the specified experiment and variant.
    /// </summary>
    /// <param name="metric">Metric to persist.</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    Task RecordMetricAsync(AbMetricRecord metric, CancellationToken ct = default);

    /// <summary>
    /// Immediately terminates the experiment, routing 100% of subsequent traffic back to the
    /// control model (edge case — new model is significantly worse).
    /// Clears the Redis active-experiment cache so the next request sees the terminated state.
    /// </summary>
    /// <param name="experimentId">Experiment to terminate.</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    Task TerminateExperimentAsync(Guid experimentId, CancellationToken ct = default);

    /// <summary>
    /// Pauses the experiment, routing all traffic to the control model without discarding
    /// collected metrics data.
    /// </summary>
    /// <param name="experimentId">Experiment to pause.</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    Task PauseExperimentAsync(Guid experimentId, CancellationToken ct = default);

    /// <summary>
    /// Aggregates per-variant metrics into a comparison report (mean accuracy, P95 latency,
    /// total cost) and performs a basic statistical significance check.
    /// </summary>
    /// <param name="experimentId">Experiment to aggregate.</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    Task<AbExperimentResult> GetExperimentResultsAsync(Guid experimentId, CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of all experiments optionally filtered by status.
    /// </summary>
    /// <param name="statusFilter">Optional status to filter by. Null returns all experiments.</param>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Page size 1–100 (default 20).</param>
    /// <param name="ct">Propagates cancellation from the caller.</param>
    Task<IReadOnlyList<AbExperiment>> ListExperimentsAsync(
        string?           statusFilter = null,
        int               page         = 1,
        int               pageSize     = 20,
        CancellationToken ct           = default);
}
