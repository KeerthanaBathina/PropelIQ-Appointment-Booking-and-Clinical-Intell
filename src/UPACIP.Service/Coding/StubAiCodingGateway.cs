using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Coding;

/// <summary>
/// Development stub for the AI coding gateway (US_047).
///
/// Always returns an empty suggestion list so the coding pipeline exercises the
/// "uncodable" edge-case path without an LLM dependency.  This class is replaced
/// by the real AI gateway adapter implemented in task_003.
///
/// Registered as the <see cref="IAiCodingGateway"/> binding in DI until task_003
/// provides a concrete implementation.
/// </summary>
public sealed class StubAiCodingGateway : IAiCodingGateway
{
    private readonly ILogger<StubAiCodingGateway> _logger;

    public StubAiCodingGateway(ILogger<StubAiCodingGateway> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AiCodingResult>> GenerateCodesAsync(
        IReadOnlyDictionary<Guid, string> diagnosisDescriptions,
        Guid                              patientIdForAudit,
        CancellationToken                 ct = default)
    {
        // Log so integration tests can verify this stub is being called.
        _logger.LogDebug(
            "StubAiCodingGateway: returning empty suggestions for {Count} diagnoses on patient {PatientId}. " +
            "Replace with real AI gateway implementation (task_003).",
            diagnosisDescriptions.Count, patientIdForAudit);

        IReadOnlyList<AiCodingResult> results = diagnosisDescriptions.Keys
            .Select(id => new AiCodingResult { DiagnosisId = id, Suggestions = [] })
            .ToList();

        return Task.FromResult(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AiCptCodingResult>> GenerateCptCodesAsync(
        IReadOnlyDictionary<Guid, string> procedureDescriptions,
        Guid                              patientIdForAudit,
        CancellationToken                 ct = default)
    {
        _logger.LogDebug(
            "StubAiCodingGateway: returning empty CPT suggestions for {Count} procedures on patient {PatientId}.",
            procedureDescriptions.Count, patientIdForAudit);

        IReadOnlyList<AiCptCodingResult> results = procedureDescriptions.Keys
            .Select(id => new AiCptCodingResult { ProcedureId = id, Suggestions = [] })
            .ToList();

        return Task.FromResult(results);
    }
}
