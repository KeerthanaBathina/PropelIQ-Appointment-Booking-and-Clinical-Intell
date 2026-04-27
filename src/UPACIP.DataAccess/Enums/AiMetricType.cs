namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the AI accuracy metric being measured.
/// Stored as a string via EF Core <c>HasConversion&lt;string&gt;()</c> (US_072 task_001).
/// </summary>
public enum AiMetricType
{
    /// <summary>
    /// AI-human agreement rate for medical coding (AIR-Q01 target: &gt;98%).
    /// </summary>
    CodingAgreement = 1,

    /// <summary>
    /// Precision rate for AI clinical data extraction (AIR-Q02 target: &gt;95%).
    /// </summary>
    ExtractionPrecision = 2,

    /// <summary>
    /// Recall rate for AI clinical data extraction (AIR-Q02 target: &gt;95%).
    /// </summary>
    ExtractionRecall = 3,
}
