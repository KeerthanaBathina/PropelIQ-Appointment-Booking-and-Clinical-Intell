namespace UPACIP.Service.Appointments;

/// <summary>
/// Staff-only service contract for walk-in registration (US_022 FR-021, FR-022).
///
/// All methods enforce that the caller has staff or admin role — the controller
/// layer is responsible for the [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
/// attribute; this interface does not re-validate identity.
/// </summary>
public interface IWalkInRegistrationService
{
    /// <summary>
    /// Searches for patient records by name, DOB, or phone (AC-2).
    /// Returns lightweight summaries suitable for staff selection.
    /// </summary>
    /// <param name="request">Search term and field selector.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Up to 20 matching patient records, ordered by relevance.</returns>
    Task<IReadOnlyList<WalkInPatientSearchResult>> SearchPatientsAsync(
        WalkInPatientSearchRequest request,
        CancellationToken          ct = default);

    /// <summary>
    /// Returns only same-day available slots for walk-in booking (AC-4).
    /// When no same-day slots exist the <see cref="SameDaySlotsResponse.NextAvailableDate"/>
    /// carries the nearest future available date to satisfy the AC-4 requirement.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<SameDaySlotsResponse> GetSameDaySlotsAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a walk-in appointment with <c>IsWalkIn = true</c> and automatically
    /// inserts a <c>QueueEntry</c> at <c>Normal</c> or <c>Urgent</c> priority (AC-3, EC-2).
    ///
    /// When <see cref="WalkInBookingRequest.PatientId"/> is null the method performs
    /// minimal inline patient creation using <see cref="WalkInBookingRequest.NewPatient"/>.
    ///
    /// Returns:
    ///   - <see cref="WalkInBookingOutcome.Success"/> + <see cref="WalkInBookingResponse"/> on success.
    ///   - <see cref="WalkInBookingOutcome.SlotUnavailable"/> when the slot is taken.
    ///   - <see cref="WalkInBookingOutcome.UrgentEscalation"/> when urgent + no same-day slots (EC-2).
    ///   - <see cref="WalkInBookingOutcome.PatientNotFound"/> when the supplied PatientId does not exist.
    ///   - <see cref="WalkInBookingOutcome.DuplicatePatientEmail"/> when inline creation would
    ///     collide with an existing email.
    /// </summary>
    Task<(WalkInBookingOutcome Outcome, WalkInBookingResponse? Response)> BookWalkInAsync(
        WalkInBookingRequest request,
        CancellationToken    ct = default);
}

// ─── Supporting types ─────────────────────────────────────────────────────────

/// <summary>
/// Response from the same-day slot availability query (AC-4).
/// </summary>
public sealed record SameDaySlotsResponse(
    /// <summary>Available same-day slots (may be empty).</summary>
    IReadOnlyList<SlotItem> Slots,
    /// <summary>
    /// ISO-8601 date of the nearest future available slot when <see cref="Slots"/> is empty.
    /// Null when same-day slots are available or when no future slots exist.
    /// </summary>
    string? NextAvailableDate);

/// <summary>
/// Discriminated outcome of the walk-in booking operation.
/// </summary>
public enum WalkInBookingOutcome
{
    /// <summary>Appointment created and patient added to the queue (AC-3).</summary>
    Success,
    /// <summary>The selected slot was already taken by the time the transaction committed.</summary>
    SlotUnavailable,
    /// <summary>
    /// Urgent walk-in but no same-day slots exist — supervisor escalation required (EC-2).
    /// No appointment is created; the caller must surface the escalation guidance.
    /// </summary>
    UrgentEscalation,
    /// <summary>The supplied PatientId does not correspond to an active patient record.</summary>
    PatientNotFound,
    /// <summary>Inline patient creation failed because the email is already registered.</summary>
    DuplicatePatientEmail,
}
