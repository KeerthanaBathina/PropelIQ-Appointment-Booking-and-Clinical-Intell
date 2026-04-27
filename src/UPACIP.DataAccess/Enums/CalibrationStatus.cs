namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Indicates whether a confidence score stored on an <c>ExtractedData</c> row has been
/// transformed by the Platt-scaling calibration pipeline (US_073 task_001, AC-1, edge case).
///
/// <para>
/// When insufficient verification data exists the system cannot fit reliable calibration
/// parameters; affected records are flagged <see cref="CalibrationPending"/> so downstream
/// consumers know to treat the raw <c>ConfidenceScore</c> as uncalibrated.
/// </para>
/// </summary>
public enum CalibrationStatus
{
    /// <summary>
    /// The <c>CalibratedConfidenceScore</c> was computed using current Platt-scaling
    /// parameters and reflects the true calibrated probability estimate.
    /// </summary>
    Calibrated = 1,

    /// <summary>
    /// No calibration has been applied.  <c>CalibratedConfidenceScore</c> is null;
    /// callers should use the raw <c>ConfidenceScore</c>.
    /// </summary>
    Uncalibrated = 2,

    /// <summary>
    /// Calibration is in progress or waiting for sufficient verification data.
    /// The calibration job will set this to <see cref="Calibrated"/> once enough
    /// verified samples have been collected (minimum 30 per edge case requirement).
    /// </summary>
    CalibrationPending = 3,
}
