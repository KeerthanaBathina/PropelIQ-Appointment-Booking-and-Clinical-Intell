namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Discrete risk band derived from the normalized 0-100 no-show risk score (AIR-006, FR-014).
///
/// Thresholds:
///   - Low    : score  0–29
///   - Medium : score 30–69
///   - High   : score 70–100 (triggers outreach flag)
///
/// Stored as a VARCHAR(10) string in PostgreSQL so migration diffs stay human-readable.
/// </summary>
public enum NoShowRiskBand
{
    Low,
    Medium,
    High,
}
