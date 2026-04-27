namespace UPACIP.Api.Features.AIGateway.Contracts;

/// <summary>
/// Classifies the AI operation being requested.
/// Used by the AI Gateway to select the correct token budget, prompt strategy,
/// and provider routing policy (AIR-O01, AIR-O02, AIR-O03).
/// </summary>
public enum AIRequestType
{
    /// <summary>
    /// Clinical document OCR / structured-data extraction via LLM.
    /// Token budget: 4 096 input / 1 024 output (AIR-O01).
    /// </summary>
    DocumentParsing = 1,

    /// <summary>
    /// Natural-language conversational intake session step.
    /// Token budget: 500 input / 200 output (AIR-O02).
    /// </summary>
    ConversationalIntake = 2,

    /// <summary>
    /// ICD-10 / CPT medical-code suggestion from clinical descriptions.
    /// Token budget: 2 048 input / 500 output (AIR-O03).
    /// </summary>
    MedicalCoding = 3,
}
