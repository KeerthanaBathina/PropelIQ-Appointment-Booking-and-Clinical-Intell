namespace UPACIP.DataAccess.Enums;

public enum AuditAction
{
    Login,
    Logout,
    DataAccess,
    DataModify,
    DataDelete,
    /// <summary>Authenticated user attempted a route outside their role (HTTP 403).</summary>
    AccessDenied,
    /// <summary>Unauthenticated or token-rejected request (HTTP 401).</summary>
    AuthFailure,
    /// <summary>Password reset was requested for a user account (FR-006).</summary>
    PasswordResetRequest,
    /// <summary>Password was successfully reset via the reset link (FR-006).</summary>
    PasswordResetSuccess,
    /// <summary>Password reset attempt failed (invalid or expired token) (FR-006).</summary>
    PasswordResetFailure,
    /// <summary>Credential validation failed (wrong password) — includes remaining attempts (US_016 AC-2).</summary>
    FailedLogin,
    /// <summary>Account locked after exceeding the maximum failed-attempt threshold (US_016 AC-2).</summary>
    AccountLocked,
    /// <summary>TOTP-based MFA was enabled for the user (US_016 AC-1).</summary>
    MfaEnabled,
    /// <summary>TOTP-based MFA was disabled by the user (US_016).</summary>
    MfaDisabled,
    /// <summary>MFA TOTP code was successfully verified during login (US_016 AC-1).</summary>
    MfaVerified,
    /// <summary>An admin reset MFA for another user (US_016 edge case).</summary>
    AdminMfaReset,
    /// <summary>Patient cancelled a scheduled appointment (US_019 AC-4, NFR-012).</summary>
    AppointmentCancelled,
    /// <summary>Patient registered on the waitlist for a fully-booked slot (US_020 AC-1).</summary>
    WaitlistRegistered,
    /// <summary>Waitlist offer notification dispatched to patient (US_020 AC-2).</summary>
    WaitlistOfferDispatched,
    /// <summary>Patient redeemed a waitlist claim link and acquired a 1-minute slot hold (US_020 AC-3).</summary>
    WaitlistClaimed,
    /// <summary>Patient explicitly removed themselves from the waitlist (US_020 EC-1).</summary>
    WaitlistRemoved,
    /// <summary>Appointment was automatically swapped to a preferred slot (US_021 AC-1).</summary>
    AppointmentAutoSwapped,
    /// <summary>Auto-swap skipped — account has auto-swap disabled by staff (US_021 AC-3).</summary>
    AutoSwapSkipped,
    /// <summary>Manual-confirmation offer sent for a preferred slot inside the 24-hour window (US_021 AC-5).</summary>
    ManualSwapOfferSent,
    /// <summary>Staff created a walk-in appointment with is_walk_in = true and inserted a queue entry (US_022 AC-3).</summary>
    WalkInBooked,
    /// <summary>Patient-role caller attempted a staff-only walk-in endpoint and was blocked (US_022 EC-1).</summary>
    WalkInUnauthorized,
    /// <summary>Urgent walk-in with no same-day capacity — supervisor escalation path surfaced (US_022 EC-2).</summary>
    WalkInUrgentEscalation,
    /// <summary>Patient rescheduled an existing appointment to a new slot (US_023 AC-1, AC-4).</summary>
    AppointmentRescheduled,
    /// <summary>Staff uploaded a clinical document with AES-256 encryption at rest (US_038 AC-2, AC-4).</summary>
    DocumentUploaded,
    /// <summary>Staff verified or corrected a single extracted clinical data row (US_041 AC-4).</summary>
    ExtractedDataVerified,
    /// <summary>Staff bulk-verified multiple extracted data rows in one operation (US_041 EC-2).</summary>
    ExtractedDataBulkVerified,
    /// <summary>Staff selected a specific data value when resolving a clinical conflict (US_045 AC-2).</summary>
    ConflictValueSelected,
    /// <summary>Staff marked both conflicting values as valid with different date attribution (US_045 EC-2).</summary>
    ConflictBothValid,
    /// <summary>Patient profile version transitioned to Verified — all conflicts resolved (US_045 AC-4).</summary>
    ProfileVerified,

    /// <summary>Staff manually verified or corrected an extracted data entry during AI-unavailable fallback or low-confidence review (US_046 AC-3, FR-093).</summary>
    ManualDataVerified,
}
