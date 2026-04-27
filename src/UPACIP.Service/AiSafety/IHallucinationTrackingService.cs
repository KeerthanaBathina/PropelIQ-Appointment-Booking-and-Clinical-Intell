using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Service for recording staff verification outcomes against AI-generated medical
/// justifications, calculating daily hallucination rates, and generating critical
/// alerts when the rate exceeds the 5% target (US_074 task_002, AC-1, AC-2, AIR-Q06).
///
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item>Record staff verification of each AI justification as Supported, Unsupported
///   (hallucination), or PartiallySupported (AC-1).</item>
///   <item>Handle retroactive hallucination detection: when staff discover unsupported data
///   after initial approval, reset <c>ApprovedByUserId</c>, create a retroactive
///   <c>HallucinationAlert</c>, and mark the entry for re-verification (edge case).</item>
///   <item>Calculate the daily hallucination rate:
///   <c>UnsupportedCount / TotalVerified</c> (AC-1).</item>
///   <item>Run the full daily aggregation cycle, persisting a <c>HallucinationMetric</c>
///   and generating a <c>HallucinationAlert</c> when rate &gt; 5% (AC-2).</item>
/// </list>
///
/// <para>Registered as Scoped — shares the DI scope with <c>ApplicationDbContext</c>.</para>
/// </summary>
public interface IHallucinationTrackingService
{
    /// <summary>
    /// Records the outcome of a staff member verifying an AI-generated medical justification
    /// against its source clinical document (AC-1).
    ///
    /// <para>
    /// Creates a <see cref="UPACIP.DataAccess.Entities.HallucinationRecord"/> with the
    /// supplied classification.  Logging uses <paramref name="medicalCodeId"/> only —
    /// no patient PII is included in log output (AIR-Q06).
    /// </para>
    /// </summary>
    /// <param name="medicalCodeId">ID of the AI-suggested <c>MedicalCode</c> being verified.</param>
    /// <param name="verifierUserId">ID of the staff member performing the verification.</param>
    /// <param name="status">
    /// Classification outcome: <see cref="SourceSupportStatus.Unsupported"/> marks a hallucination.
    /// </param>
    /// <param name="notes">Optional staff explanation for the classification (no PII).</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordVerificationAsync(
        Guid               medicalCodeId,
        Guid               verifierUserId,
        SourceSupportStatus status,
        string?             notes,
        CancellationToken   ct = default);

    /// <summary>
    /// Handles the edge case where staff discover a hallucination in a previously-approved
    /// AI justification.
    ///
    /// <para>Performs the following atomically:</para>
    /// <list type="number">
    ///   <item>Creates or updates the <c>HallucinationRecord</c> for <paramref name="medicalCodeId"/>
    ///   with <c>SourceSupportStatus = Unsupported</c> and <c>IsRetroactive = true</c>.</item>
    ///   <item>Resets <c>MedicalCode.ApprovedByUserId = null</c> to require re-approval.</item>
    ///   <item>Creates a <c>HallucinationAlert</c> with <c>IsRetroactive = true</c> and
    ///   a recommendation referencing the affected <paramref name="medicalCodeId"/>.</item>
    /// </list>
    ///
    /// Logs at Warning level using <paramref name="medicalCodeId"/> only (no PII).
    /// </summary>
    /// <param name="medicalCodeId">ID of the previously-approved <c>MedicalCode</c>.</param>
    /// <param name="reporterUserId">ID of the user reporting the retroactive hallucination.</param>
    /// <param name="reason">Staff-provided reason for the retroactive finding (no PII).</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordRetroactiveHallucinationAsync(
        Guid              medicalCodeId,
        Guid              reporterUserId,
        string            reason,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates the hallucination rate for the given <paramref name="date"/>.
    /// </summary>
    /// <param name="date">Calendar date (UTC) to calculate the rate for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Rate as 0–1 decimal (e.g. <c>0.04</c> = 4 %).
    /// Returns <c>0.0</c> when no verifications exist for the date.
    /// </returns>
    Task<double> CalculateDailyRateAsync(DateTime date, CancellationToken ct = default);

    /// <summary>
    /// Executes the full daily aggregation cycle for the previous calendar day:
    /// calculates the hallucination rate, persists a <c>HallucinationMetric</c>,
    /// and generates a <c>HallucinationAlert</c> when rate exceeds 5% (AC-2).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RunDailyAggregationAsync(CancellationToken ct = default);
}
