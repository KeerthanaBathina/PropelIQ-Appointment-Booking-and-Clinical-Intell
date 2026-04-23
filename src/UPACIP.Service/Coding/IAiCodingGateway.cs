namespace UPACIP.Service.Coding;

/// <summary>
/// Represents a single ICD-10 code suggestion returned by the AI coding gateway (US_047, AC-1, AC-4).
/// </summary>
public sealed record AiCodeSuggestion
{
    /// <summary>ICD-10 code value (e.g. <c>"J18.9"</c>).</summary>
    public string CodeValue { get; init; } = string.Empty;

    /// <summary>Clinical description of the code.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>AI confidence in the range [0.0, 1.0].</summary>
    public float Confidence { get; init; }

    /// <summary>AI-generated explanation of the mapping rationale (AC-1).</summary>
    public string Justification { get; init; } = string.Empty;

    /// <summary>
    /// Relevance rank among sibling codes for the same diagnosis (1 = most relevant, AC-4).
    /// <c>null</c> when the AI returns only a single code for the diagnosis.
    /// </summary>
    public int? RelevanceRank { get; init; }
}

/// <summary>
/// Structured result for one extracted diagnosis processed by the AI coding gateway.
/// </summary>
public sealed record AiCodingResult
{
    /// <summary>The <c>ExtractedData</c> primary key this result corresponds to.</summary>
    public Guid DiagnosisId { get; init; }

    /// <summary>
    /// One or more code suggestions ordered by relevance (AC-4).
    /// An empty list indicates the AI could not find a matching code (uncodable edge case).
    /// </summary>
    public IReadOnlyList<AiCodeSuggestion> Suggestions { get; init; } = [];
}

/// <summary>
/// Contract for the AI coding gateway that maps extracted diagnoses to ICD-10 codes.
///
/// This interface is consumed by <see cref="IIcd10CodingService"/> and implemented by
/// the AI gateway adapter in task_003.  A stub implementation is registered during
/// development so the coding pipeline can be tested end-to-end before task_003 is
/// complete.
///
/// Design contract:
/// <list type="bullet">
///   <item>Never throws — failures are expressed via an empty <see cref="AiCodingResult.Suggestions"/> list.</item>
///   <item>PII must be redacted from prompts before any external API call (AIR-S01).</item>
///   <item>All prompts and responses are logged with a correlation ID in the caller (AIR-S04).</item>
///   <item>One result per diagnosis ID is always returned, even on partial AI failure.</item>
/// </list>
/// </summary>
public interface IAiCodingGateway
{
    /// <summary>
    /// Sends the given extracted diagnosis descriptions to the AI model and returns
    /// ICD-10 code suggestions for each one.
    ///
    /// The implementation in task_003 uses GPT-4o-mini with Claude 3.5 Sonnet fallback
    /// and RAG-grounded retrieval from the <c>icd10_code_library</c> pgvector index.
    ///
    /// This stub always returns an empty suggestion list so integration tests can verify
    /// the uncodable-handling path without an LLM dependency (edge case coverage).
    /// </summary>
    /// <param name="diagnosisDescriptions">
    /// Map of <c>ExtractedData</c> ID → raw diagnosis text to code.
    /// </param>
    /// <param name="patientIdForAudit">
    /// Opaque correlation reference used in audit logs only — never sent to the LLM.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One <see cref="AiCodingResult"/> per input diagnosis ID.</returns>
    Task<IReadOnlyList<AiCodingResult>> GenerateCodesAsync(
        IReadOnlyDictionary<Guid, string> diagnosisDescriptions,
        Guid                              patientIdForAudit,
        CancellationToken                 ct = default);

    /// <summary>
    /// Sends the given extracted procedure descriptions to the AI model and returns
    /// CPT code suggestions for each one (US_048, AIR-003, AC-1, AC-3).
    ///
    /// Uses GPT-4o-mini with Claude 3.5 Sonnet fallback and RAG-grounded retrieval
    /// from the CPT coding guideline pgvector index.
    ///
    /// Never throws — failures are expressed via an empty <see cref="AiCptCodingResult.Suggestions"/> list.
    /// </summary>
    /// <param name="procedureDescriptions">
    /// Map of <c>ExtractedData</c> ID → sanitised procedure text.
    /// </param>
    /// <param name="patientIdForAudit">
    /// Opaque correlation reference used in audit logs only — never sent to the LLM (AIR-S01).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One <see cref="AiCptCodingResult"/> per input procedure ID.</returns>
    Task<IReadOnlyList<AiCptCodingResult>> GenerateCptCodesAsync(
        IReadOnlyDictionary<Guid, string> procedureDescriptions,
        Guid                              patientIdForAudit,
        CancellationToken                 ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// CPT coding result types (US_048)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single CPT code suggestion returned by the AI coding gateway (US_048, AC-1, AC-3).
/// </summary>
public sealed record AiCptCodeSuggestion
{
    /// <summary>CPT code value (5-digit numeric, e.g. <c>"99213"</c>).</summary>
    public string CodeValue { get; init; } = string.Empty;

    /// <summary>Clinical description of the procedure code.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>AI confidence in the range [0.0, 1.0].</summary>
    public float Confidence { get; init; }

    /// <summary>AI-generated explanation of the mapping rationale (AC-1).</summary>
    public string Justification { get; init; } = string.Empty;

    /// <summary>
    /// Relevance rank among sibling codes for the same procedure (1 = most relevant, AC-3).
    /// <c>null</c> when the AI returns only a single code for the procedure.
    /// </summary>
    public int? RelevanceRank { get; init; }

    /// <summary>
    /// <c>true</c> when this code represents a bundled billing unit (AC-3 edge case).
    /// The bundled code is presented at relevance_rank 1 alongside the individual component codes.
    /// </summary>
    public bool IsBundled { get; init; }

    /// <summary>
    /// CPT component codes included in this bundle (only populated when <see cref="IsBundled"/> is <c>true</c>).
    /// </summary>
    public IReadOnlyList<string>? BundleComponents { get; init; }
}

/// <summary>
/// Structured result for one extracted procedure processed by the AI CPT coding gateway.
/// </summary>
public sealed record AiCptCodingResult
{
    /// <summary>The <c>ExtractedData</c> primary key this result corresponds to.</summary>
    public Guid ProcedureId { get; init; }

    /// <summary>
    /// One or more CPT code suggestions ordered by relevance (AC-3).
    /// An empty list indicates the AI could not find a matching code (uncodable edge case).
    /// </summary>
    public IReadOnlyList<AiCptCodeSuggestion> Suggestions { get; init; } = [];
}
