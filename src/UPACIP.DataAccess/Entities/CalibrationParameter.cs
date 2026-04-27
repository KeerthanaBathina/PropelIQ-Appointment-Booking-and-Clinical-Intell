using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Stores the Platt-scaling calibration parameters for a single clinical data category
/// (US_073 task_001, AC-1, AC-3, edge case).
///
/// <para>
/// Platt scaling maps raw AI confidence scores to calibrated probabilities via the
/// sigmoid function: <c>P(y=1) = 1 / (1 + exp(Slope * raw_score + Intercept))</c>.
/// One active parameter set is maintained per <see cref="DataType"/> (Medication, Diagnosis,
/// Procedure, Allergy) to support independent per-category calibration (edge case).
/// </para>
///
/// <para>
/// When a new weekly calibration run completes it deactivates the previous row by setting
/// <see cref="IsActive"/> to <c>false</c> and inserts a new active row.  The unique filtered
/// index on (<c>DataType</c>, <c>IsActive</c>) WHERE <c>is_active = true</c> enforces exactly
/// one active calibration per category at the database level.
/// </para>
/// </summary>
public sealed class CalibrationParameter : BaseEntity
{
    /// <summary>
    /// Clinical data category these parameters apply to.
    /// Calibration is performed independently per category (edge case).
    /// </summary>
    public DataType DataType { get; set; }

    /// <summary>
    /// Platt-scaling slope parameter (A in the sigmoid formula).
    /// Negative values are typical: a more negative slope steepens the calibration curve.
    /// </summary>
    public double Slope { get; set; }

    /// <summary>
    /// Platt-scaling intercept parameter (B in the sigmoid formula).
    /// Shifts the calibration curve along the raw-score axis.
    /// </summary>
    public double Intercept { get; set; }

    /// <summary>
    /// UTC timestamp of the calibration run that produced these parameters.
    /// </summary>
    public DateTime LastCalibratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of verified <see cref="ExtractedData"/> rows used to fit these parameters.
    /// Must be ≥ 30 (minimum sample size) for the parameters to be considered reliable.
    /// </summary>
    public int VerificationSampleSize { get; set; }

    /// <summary>
    /// <c>true</c> when this is the currently active parameter set for the given
    /// <see cref="DataType"/>.  The unique filtered index on (DataType, IsActive) enforces
    /// at most one active row per category.
    /// </summary>
    public bool IsActive { get; set; }
}
