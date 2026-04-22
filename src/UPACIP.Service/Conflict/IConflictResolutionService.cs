namespace UPACIP.Service.Conflict;

/// <summary>
/// Contract for the staff conflict resolution workflow service (US_045, AC-2, AC-4, EC-1, EC-2, FR-054).
///
/// Extends the base conflict lifecycle managed by <see cref="IConflictManagementService"/> with
/// value-selection semantics, profile consolidation, and verification status management:
/// <list type="bullet">
///   <item>
///     <see cref="SelectConflictValueAsync"/> — staff selects the correct data value from the
///     conflicting sources; the chosen value is persisted to the consolidated profile (AC-2).
///   </item>
///   <item>
///     <see cref="ResolveBothValidAsync"/> — staff confirms both values are clinically valid
///     with different date attribution; both entries are preserved (EC-2).
///   </item>
///   <item>
///     <see cref="CheckAndUpdateProfileVerificationAsync"/> — transitions
///     <c>PatientProfileVersion.verification_status</c> to Verified/PartiallyVerified when
///     all/some conflicts are resolved; creates an audit log entry on full verification (AC-4).
///   </item>
///   <item>
///     <see cref="GetResolutionProgressAsync"/> — returns resolved/total counts and current
///     verification status so partial progress survives navigation away (EC-1).
///   </item>
/// </list>
/// </summary>
public interface IConflictResolutionService
{
    /// <summary>
    /// Resolves a conflict by selecting one of the conflicting data values as authoritative (AC-2).
    ///
    /// Behaviour:
    /// <list type="bullet">
    ///   <item>Validates the conflict is Detected or UnderReview.</item>
    ///   <item>Validates <see cref="SelectValueRequest.SelectedExtractedDataId"/> belongs to the conflict's source IDs.</item>
    ///   <item>Writes the selected extracted data value into the consolidated <c>PatientProfileVersion</c>.</item>
    ///   <item>Marks the conflict Resolved with <c>resolution_type = SelectedValue</c> and staff attribution.</item>
    ///   <item>Invalidates the patient's Redis-cached profile.</item>
    ///   <item>Calls <see cref="CheckAndUpdateProfileVerificationAsync"/> to advance the profile status.</item>
    /// </list>
    /// </summary>
    /// <param name="request">Selection request — conflict ID, chosen extracted data ID, user, notes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SelectConflictValueAsync(SelectValueRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resolves a conflict by confirming both conflicting values are valid with distinct date
    /// attribution (EC-2 — "Both Valid — Different Dates").
    ///
    /// Behaviour:
    /// <list type="bullet">
    ///   <item>Validates the conflict is Detected or UnderReview.</item>
    ///   <item>Validates the explanation is not empty.</item>
    ///   <item>Marks the conflict Resolved with <c>resolution_type = BothValid</c> and stores the explanation.</item>
    ///   <item>Ensures both source extracted data values are preserved in the consolidated profile.</item>
    ///   <item>Invalidates the patient's Redis-cached profile.</item>
    ///   <item>Calls <see cref="CheckAndUpdateProfileVerificationAsync"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="request">BothValid request — conflict ID, user, explanation.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResolveBothValidAsync(BothValidRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks the open-conflict count for the patient and advances
    /// <c>PatientProfileVersion.verification_status</c> accordingly (AC-4).
    ///
    /// Transitions:
    /// <list type="bullet">
    ///   <item>0 open conflicts → <c>Verified</c> + sets verified_by_user_id / verified_at + audit log.</item>
    ///   <item>Some open but fewer than total → <c>PartiallyVerified</c>.</item>
    ///   <item>All open (no progress yet) → <c>Unverified</c> (no change).</item>
    /// </list>
    /// The audit log entry is created only when the status transitions INTO <c>Verified</c>
    /// for the first time to prevent duplicate log rows.
    /// </summary>
    /// <param name="patientId">Patient whose profile version status should be updated.</param>
    /// <param name="userId">Staff user who triggered the update (for verified_by attribution).</param>
    /// <param name="ct">Cancellation token.</param>
    Task CheckAndUpdateProfileVerificationAsync(Guid patientId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns resolution progress for the patient's conflict list (EC-1).
    ///
    /// Allows the staff UI to resume partial work after navigation and display a progress
    /// bar showing how many conflicts remain.
    /// </summary>
    /// <param name="patientId">Patient whose progress to query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="ResolutionProgressDto"/> with total/resolved/remaining counts, percentage,
    /// and current verification status.
    /// </returns>
    Task<ResolutionProgressDto> GetResolutionProgressAsync(Guid patientId, CancellationToken ct = default);
}
