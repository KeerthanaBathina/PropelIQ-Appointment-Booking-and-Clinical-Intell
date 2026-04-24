namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the type of disagreement recorded in a <c>CodingDiscrepancy</c> row (US_050 EC-1, FR-068).
/// <para>
/// Partial overrides are explicitly tracked as disagreements so they are counted
/// against the agreement rate alongside full overrides.
/// </para>
/// </summary>
public enum DiscrepancyType
{
    /// <summary>Staff replaced the AI-suggested code entirely with a different code.</summary>
    FullOverride,

    /// <summary>
    /// Staff kept the base code but modified supporting fields (e.g. description or specificity).
    /// Treated as a disagreement for agreement-rate calculation purposes (US_050 EC-1).
    /// </summary>
    PartialOverride,

    /// <summary>
    /// AI suggested a single code but staff mapped the encounter to multiple codes,
    /// or vice versa.
    /// </summary>
    MultipleCodes,
}
