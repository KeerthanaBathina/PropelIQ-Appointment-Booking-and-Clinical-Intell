namespace UPACIP.Service.Coding;

/// <summary>
/// Result of a CPT code generation run (US_048, AC-1, AC-3).
/// </summary>
public sealed record CptCodingRunResult
{
    /// <summary>Number of CPT <c>MedicalCode</c> rows inserted or updated.</summary>
    public int CodesInserted { get; init; }

    /// <summary>IDs of procedures that could not be mapped to a CPT code (uncodable edge case).</summary>
    public IReadOnlyList<Guid> UnmappedProcedureIds { get; init; } = [];
}

/// <summary>
/// Contract for the async CPT procedure code generation pipeline (US_048, AC-1, AC-3, AIR-003).
///
/// Invoked by <c>CptCodingWorker</c> for each job drained from the Redis queue.
/// Orchestrates: load extracted procedures → AI gateway → library validation →
/// bundle detection → persist MedicalCode rows → invalidate cache.
/// </summary>
public interface ICptGenerationService
{
    /// <summary>
    /// Generates AI CPT code suggestions for the given extracted procedures and persists
    /// them as <c>MedicalCode</c> rows with <c>CodeType.Cpt</c>.
    ///
    /// Idempotent: re-running with the same inputs updates existing rows rather than
    /// duplicating them (NFR-034).
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="procedureIds">IDs of <c>ExtractedData</c> rows to process.</param>
    /// <param name="correlationId">Request trace correlation ID for structured logging.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CptCodingRunResult> GenerateCptCodesAsync(
        Guid                patientId,
        IReadOnlyList<Guid> procedureIds,
        string              correlationId,
        CancellationToken   ct = default);
}
