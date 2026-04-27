namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the AI operation that generated a cost record.
/// Mirrors <c>UPACIP.Api.Features.AIGateway.Contracts.AIRequestType</c> but lives in
/// the DataAccess layer so that persistence entities remain independent of the API project.
///
/// Values must stay in sync with the API enum counterpart.
/// Stored as a string via EF Core <c>HasConversion&lt;string&gt;()</c> (US_071 TASK_001).
/// </summary>
public enum AiRequestType
{
    /// <summary>
    /// Clinical document OCR / structured-data extraction via LLM (AIR-O01).
    /// Token budget: 4 096 input / 1 024 output.
    /// </summary>
    DocumentParsing = 1,

    /// <summary>
    /// Natural-language conversational intake session step (AIR-O02).
    /// Token budget: 500 input / 200 output.
    /// </summary>
    ConversationalIntake = 2,

    /// <summary>
    /// ICD-10 / CPT medical-code suggestion from clinical descriptions (AIR-O03).
    /// Token budget: 2 048 input / 500 output.
    /// </summary>
    MedicalCoding = 3,
}
