namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the AI operation type for latency tracking.
/// Stored as a string via EF Core <c>HasConversion&lt;string&gt;()</c> (US_072 task_001).
/// </summary>
public enum AiOperationType
{
    /// <summary>
    /// Conversational patient intake operation (AC-3 target: P95 &lt;1 s).
    /// </summary>
    Intake = 1,

    /// <summary>
    /// Clinical document OCR and structured-data parsing operation (AC-3 target: P95 &lt;30 s).
    /// </summary>
    DocumentParsing = 2,

    /// <summary>
    /// AI-assisted medical coding operation (AC-3 target: P95 &lt;5 s).
    /// </summary>
    MedicalCoding = 3,
}
