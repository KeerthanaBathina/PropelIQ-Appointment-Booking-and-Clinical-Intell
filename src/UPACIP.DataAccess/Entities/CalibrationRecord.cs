using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Records the outcome of a single weekly calibration run for one confidence bin within
/// one clinical data category (US_073 task_001, AC-3).
///
/// <para>
/// Each weekly run compares the AI's predicted confidence against the actual accuracy
/// observed from staff verifications.  The run produces one row per confidence bin
/// (e.g. 0–10%, 10–20%, …, 90–100%) per <see cref="DataType"/>, capturing the
/// per-bin predicted vs. actual accuracy and flagging rows where the gap exceeds 5%
/// for drift detection (AC-4).
/// </para>
/// </summary>
public sealed class CalibrationRecord : BaseEntity
{
    /// <summary>
    /// UTC date the weekly calibration job was executed.
    /// </summary>
    public DateTime CalibrationRunDate { get; set; }

    /// <summary>
    /// Clinical data category evaluated in this record.
    /// Independent per-category records enable per-type drift analysis (edge case).
    /// </summary>
    public DataType DataType { get; set; }

    /// <summary>
    /// Mean predicted confidence score for items in this bin, as a percentage (0–100).
    /// Computed as the average of <c>CalibratedConfidenceScore * 100</c> for verified items
    /// whose calibrated score falls in [<see cref="BinStart"/>, <see cref="BinEnd"/>).
    /// </summary>
    public double PredictedAccuracy { get; set; }

    /// <summary>
    /// Actual correctness rate for items in this bin, as a percentage (0–100).
    /// Computed as: <c>(correct verifications / total verifications) * 100</c>.
    /// </summary>
    public double ActualAccuracy { get; set; }

    /// <summary>
    /// Absolute gap between <see cref="PredictedAccuracy"/> and <see cref="ActualAccuracy"/>.
    /// <c>DriftPercentage = |PredictedAccuracy - ActualAccuracy|</c>.
    /// Values above 5 trigger alert generation (AC-4).
    /// </summary>
    public double DriftPercentage { get; set; }

    /// <summary>
    /// Lower bound (inclusive) of this confidence bin as an integer percentage (0–100).
    /// E.g. <c>80</c> for the 80–90% bin.
    /// </summary>
    public int BinStart { get; set; }

    /// <summary>
    /// Upper bound (exclusive) of this confidence bin as an integer percentage (0–100).
    /// E.g. <c>90</c> for the 80–90% bin.
    /// </summary>
    public int BinEnd { get; set; }

    /// <summary>
    /// Number of verified <see cref="ExtractedData"/> items that fell into this bin during
    /// the calibration run.  Bins with fewer than 30 items are flagged as
    /// <see cref="Enums.CalibrationStatus.CalibrationPending"/> (edge case).
    /// </summary>
    public int SampleSize { get; set; }

    /// <summary>
    /// <c>true</c> when <see cref="DriftPercentage"/> exceeds the 5% threshold (AC-4).
    /// Set by the calibration job; triggers <see cref="CalibrationDriftAlert"/> creation.
    /// </summary>
    public bool DriftDetected { get; set; }
}
