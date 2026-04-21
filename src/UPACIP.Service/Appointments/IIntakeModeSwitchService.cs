namespace UPACIP.Service.Appointments;

/// <summary>
/// Orchestration service for bidirectional intake mode switching (US_029, FR-028).
///
/// Responsibilities:
///   - Switch AI → Manual: merge AI-collected fields into the manual draft, record
///     prefilled key metadata, detect and record conflicts (EC-1, AC-1, AC-4).
///   - Switch Manual → AI: merge manual-form values into the active AI session,
///     compute next uncollected field, detect and record conflicts (EC-1, AC-2, AC-4).
///   - Probe AI availability: cheap health check so the UI can disable switch-to-AI
///     before a patient triggers a failing transition (EC-2).
///
/// Conflict resolution (EC-1):
///   When the same field has a value from both AI and manual intake the most recently
///   entered value wins. The losing value is returned in <see cref="IntakeFieldConflict"/>
///   so the UI can show a non-blocking attribution note (AC-4).
///
/// Ownership: all methods take a <paramref name="patientId"/> resolved server-side from
/// the JWT — never trusted from the request body (OWASP A01).
/// </summary>
public interface IIntakeModeSwitchService
{
    /// <summary>
    /// Switches the patient from AI intake to the manual form.
    ///
    /// Merges all AI-collected field values into the manual draft snapshot, marks
    /// prefilled keys, and returns the combined field map ready to pre-populate the
    /// manual form (AC-1, AC-3).
    ///
    /// Idempotent: calling this when no active AI session exists returns an empty field
    /// map (the manual form loads fresh from the existing draft, if any).
    /// </summary>
    Task<SwitchToManualModeResponse> SwitchToManualAsync(
        Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Switches the patient from the manual form to AI intake.
    ///
    /// Merges current manual field values into the active AI session (or creates a new session),
    /// computes the next uncollected field, and returns the session ID to resume (AC-2, AC-3).
    ///
    /// Returns <c>null</c> when the AI service is unavailable (EC-2 — caller returns 503).
    /// </summary>
    Task<SwitchToAIResponse?> SwitchToAIAsync(
        Guid patientId,
        SwitchToAIRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a lightweight AI availability indicator without creating or modifying any
    /// session state (EC-2). Used to pre-disable the switch-to-AI button when AI is degraded.
    /// </summary>
    Task<AIAvailabilityResponse> CheckAIAvailabilityAsync(CancellationToken ct = default);
}
