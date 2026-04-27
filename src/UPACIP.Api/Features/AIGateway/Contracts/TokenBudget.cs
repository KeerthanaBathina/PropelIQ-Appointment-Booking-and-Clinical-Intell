namespace UPACIP.Api.Features.AIGateway.Contracts;

/// <summary>
/// Per-request-type token budget enforced by <c>AIRequestValidationMiddleware</c>.
/// Limits are derived from AIR-O01 (DocumentParsing), AIR-O02 (ConversationalIntake),
/// and AIR-O03 (MedicalCoding) to prevent runaway token consumption.
/// </summary>
/// <param name="MaxInputTokens">Maximum number of input tokens allowed for the request.</param>
/// <param name="MaxOutputTokens">Maximum number of output tokens the provider may generate.</param>
public sealed record TokenBudget(int MaxInputTokens, int MaxOutputTokens)
{
    // ── Built-in budget presets (AIR-O01, AIR-O02, AIR-O03) ──────────────────

    /// <summary>DocumentParsing budget: 4 096 input / 1 024 output (AIR-O01).</summary>
    public static readonly TokenBudget DocumentParsing = new(4_096, 1_024);

    /// <summary>ConversationalIntake budget: 500 input / 200 output (AIR-O02).</summary>
    public static readonly TokenBudget ConversationalIntake = new(500, 200);

    /// <summary>MedicalCoding budget: 2 048 input / 500 output (AIR-O03).</summary>
    public static readonly TokenBudget MedicalCoding = new(2_048, 500);

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the canonical token budget for a given <see cref="AIRequestType"/>.
    /// These ceilings may be further narrowed by configuration overrides in
    /// <see cref="UPACIP.Api.Features.AIGateway.Configuration.AIGatewayOptions"/>.
    /// </summary>
    public static TokenBudget For(AIRequestType requestType) => requestType switch
    {
        AIRequestType.DocumentParsing     => DocumentParsing,
        AIRequestType.ConversationalIntake => ConversationalIntake,
        AIRequestType.MedicalCoding       => MedicalCoding,
        _                                 => throw new ArgumentOutOfRangeException(
                                                nameof(requestType), requestType, null),
    };
}
