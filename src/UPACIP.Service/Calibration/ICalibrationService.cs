using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Calibration;

/// <summary>
/// Confidence score calibration service (US_073 AC-1 through AC-4).
///
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item>Transform raw AI confidence scores into calibrated probabilities via Platt scaling
///   using the active <c>CalibrationParameter</c> for the given <c>DataType</c> (AC-1).</item>
///   <item>Flag <c>ExtractedData</c> records with calibrated score &lt; 0.80 for mandatory
///   manual review (AC-2).</item>
///   <item>Execute the weekly calibration workflow: compute per-bin predicted-vs-actual
///   accuracy from staff verifications, fit new Platt parameters, persist
///   <c>CalibrationRecord</c> entries, and rotate active parameters (AC-3).</item>
///   <item>Detect calibration drift and generate <c>CalibrationDriftAlert</c> records
///   when the gap between predicted and actual accuracy exceeds 5% (AC-4).</item>
///   <item>Fall back to uncalibrated raw scores and mark records
///   <c>CalibrationStatus.CalibrationPending</c> when insufficient verification data
///   exists (edge case).</item>
/// </list>
/// </summary>
public interface ICalibrationService
{
    /// <summary>
    /// Applies the active Platt-scaling parameters for <paramref name="dataType"/> to
    /// <paramref name="rawScore"/> and returns the calibrated probability in [0, 1].
    ///
    /// <para>
    /// If no active <c>CalibrationParameter</c> exists for the data type (insufficient
    /// verification data), the raw score is returned unchanged and the
    /// <c>ExtractedData</c> record for <paramref name="extractedDataId"/> is marked
    /// <c>CalibrationStatus.CalibrationPending</c> (edge case).
    /// </para>
    /// </summary>
    /// <param name="rawScore">Raw confidence score in [0, 1] produced by the AI model.</param>
    /// <param name="dataType">Clinical data category of the extraction (AC-1, edge case).</param>
    /// <param name="extractedDataId">PK of the <c>ExtractedData</c> row to update.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    /// <returns>Calibrated confidence score in [0, 1].</returns>
    Task<float> CalibrateScoreAsync(
        float             rawScore,
        DataType          dataType,
        Guid              extractedDataId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the calibrated (or raw, if uncalibrated) confidence score of the
    /// <c>ExtractedData</c> record identified by <paramref name="extractedDataId"/>.
    /// Sets <c>FlaggedForReview = true</c> when the score is below 0.80 (AC-2).
    /// </summary>
    /// <param name="extractedDataId">PK of the <c>ExtractedData</c> row to evaluate.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    Task FlagLowConfidenceAsync(
        Guid              extractedDataId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the full weekly calibration workflow for all <see cref="DataType"/> values:
    /// queries staff-verified <c>ExtractedData</c> records, bins by confidence range,
    /// computes predicted-vs-actual accuracy, fits Platt parameters, persists
    /// <c>CalibrationRecord</c> rows, rotates active <c>CalibrationParameter</c> records,
    /// and generates <c>CalibrationDriftAlert</c> records on drift &gt; 5% (AC-3, AC-4).
    /// </summary>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    Task RunWeeklyCalibrationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the number of staff-verified <c>ExtractedData</c> records
    /// for <paramref name="dataType"/> in the past 90 days meets or exceeds
    /// <see cref="PlattScaler.MinimumSampleSize"/> (50).
    /// </summary>
    /// <param name="dataType">Clinical data category to check.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    Task<bool> HasSufficientDataAsync(
        DataType          dataType,
        CancellationToken cancellationToken = default);
}
