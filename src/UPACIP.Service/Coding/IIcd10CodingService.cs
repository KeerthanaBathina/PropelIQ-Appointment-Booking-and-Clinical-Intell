namespace UPACIP.Service.Coding;

/// <summary>
/// Result of an ICD-10 coding run persisted to the database (US_047, AC-1, AC-4).
/// </summary>
public sealed record Icd10CodingRunResult
{
    /// <summary>Number of <c>MedicalCode</c> rows inserted.</summary>
    public int CodesInserted { get; init; }

    /// <summary>
    /// IDs of diagnoses for which no matching code was found; these have a corresponding
    /// <c>"UNCODABLE"</c> entry in <c>medical_codes</c> (edge case).
    /// </summary>
    public IReadOnlyList<Guid> UnmappedDiagnosisIds { get; init; } = [];
}

/// <summary>
/// Contract for the ICD-10 coding service that orchestrates AI-driven code generation,
/// library validation, persistence, and the uncodable fallback (US_047, AC-1, AC-4).
///
/// The service is invoked by the <c>Icd10CodingWorker</c> background service that drains
/// the Redis coding queue, ensuring the HTTP request returns immediately with 202 Accepted
/// while heavy LLM processing runs asynchronously (NFR-029, AIR-O07).
/// </summary>
public interface IIcd10CodingService
{
    /// <summary>
    /// Reads the extracted diagnosis data for the supplied IDs, calls the
    /// <see cref="IAiCodingGateway"/> for code suggestions, validates each suggestion
    /// against the current <c>icd10_code_library</c>, ranks multiple codes by relevance
    /// (AC-4), persists results to <c>medical_codes</c>, and returns a run summary.
    ///
    /// Uncodable diagnoses (AI returns no match or confidence == 0): a <c>MedicalCode</c>
    /// row with <c>code_value = "UNCODABLE"</c>, <c>ai_confidence_score = 0.00</c>, and
    /// <c>justification = "No matching ICD-10 code found"</c> is inserted and the ID is
    /// added to <c>UnmappedDiagnosisIds</c> in the returned result (edge case).
    ///
    /// Idempotency: if a <c>MedicalCode</c> row already exists for the
    /// (patientId, CodeType.Icd10, codeValue) key, the row is updated rather than
    /// duplicated (NFR-034).
    /// </summary>
    /// <param name="patientId">Patient whose diagnoses should be coded.</param>
    /// <param name="diagnosisIds">IDs of <c>ExtractedData</c> rows to process.</param>
    /// <param name="correlationId">Request correlation ID for structured audit logging (NFR-035).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Icd10CodingRunResult> GenerateIcd10CodesAsync(
        Guid                  patientId,
        IReadOnlyList<Guid>   diagnosisIds,
        string                correlationId,
        CancellationToken     ct = default);

    /// <summary>
    /// Returns all pending (not yet approved) ICD-10 <c>MedicalCode</c> entries for
    /// the given patient, sorted by relevance rank ascending then created-at descending.
    /// Results are cached for 5 minutes (NFR-030).
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<UPACIP.DataAccess.Entities.MedicalCode>> GetPendingCodesAsync(
        Guid              patientId,
        CancellationToken ct = default);
}
