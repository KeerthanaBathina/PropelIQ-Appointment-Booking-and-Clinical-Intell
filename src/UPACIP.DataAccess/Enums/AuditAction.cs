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

    /// <summary>Staff approved an AI-suggested CPT code (US_048 AC-1, FR-066, NFR-012).</summary>
    CptCodeApproved,

    /// <summary>Staff overrode an AI-suggested CPT code with a replacement and justification (US_048 edge case, HIPAA audit requirement).</summary>
    CptCodeOverridden,

    /// <summary>Admin applied a quarterly CPT code library update (US_048 AC-4, FR-066).</summary>
    CptLibraryRefreshed,

    // ── US_049 — Code verification audit trail (AC-2, AC-4, FR-066) ──────────

    /// <summary>Staff approved an AI-suggested medical code (ICD-10 or CPT) — changes status to Verified (US_049 AC-2).</summary>
    CodeVerified,

    /// <summary>Staff overrode an AI-suggested medical code — records old code, new code, and justification (US_049 AC-4).</summary>
    CodeOverridden,

    /// <summary>Approval blocked because the target code is deprecated in the library (US_049 edge case EC-1).</summary>
    DeprecatedCodeBlocked,

    /// <summary>A medical code was re-evaluated against the current code library (US_049 AC-4).</summary>
    CodeRevalidated,

    // ── US_052 — Patient Arrival Status (AC-1, AC-2, AC-3) ──────────────────

    /// <summary>Staff marked a patient as arrived — QueueEntry created with arrival_timestamp (US_052 AC-1).</summary>
    ArrivalMarked,

    /// <summary>Staff cancelled a queue slot — appointment slot released for walk-ins (US_052 AC-3).</summary>
    ArrivalCancelled,

    /// <summary>Background service auto-marked a patient as no-show after 15-minute threshold (US_052 AC-2).</summary>
    NoShowAutoDetected,

    /// <summary>Staff overrode a no-show status to arrived-late with a provided reason (US_052 edge case).</summary>
    NoShowOverridden,

    // ── US_054 — Priority Queue Management (AC-1, AC-3, AC-4) ───────────────

    /// <summary>Staff changed a queue entry's priority to Urgent or Normal — triggers automatic re-positioning (US_054 AC-1, AC-4).</summary>
    QueuePriorityChanged,

    /// <summary>Staff manually reordered a queue entry using drag-and-drop or arrow controls (US_054 AC-2, AC-3).</summary>
    QueueReordered,

    // ── US_055 — Auto No-Show Detection & Wait Threshold Config (AC-3, AC-4) ─

    /// <summary>Admin updated the configurable wait time alert threshold (US_055 AC-3).</summary>
    WaitThresholdConfigChanged,

    // ── US_061 — Staff Account Management (AC-1, AC-3, AC-4) ─────────────────

    /// <summary>Admin created a new staff or admin account via the user management screen (US_061 AC-1).</summary>
    StaffAccountCreated,

    /// <summary>Admin deactivated a staff account — account disabled; all historical data preserved (US_061 AC-3, FR-088).</summary>
    StaffAccountDeactivated,

    /// <summary>Admin reactivated a previously deactivated staff account — previous role and permissions restored (US_061 AC-4).</summary>
    StaffAccountReactivated,

    // ── US_065 — Session Security Hardening ──────────────────────────────────

    /// <summary>
    /// An active session on Device A was terminated because the same user authenticated from
    /// Device B (concurrent session replacement, NFR-015, US_065 AC-2).
    /// </summary>
    SessionReplaced,

    // ── US_065 Task 3 — Lockout Recovery Schema ───────────────────────────────

    /// <summary>
    /// A database administrator manually unlocked an admin account using the emergency
    /// recovery SQL script (admin-lockout-recovery.sql). HIPAA-auditable event (FR-093).
    /// Recorded by the recovery script itself rather than the application layer.
    /// </summary>
    AdminManualUnlock,

    // ── US_069 TASK_003 — AI Model Version Rollback ───────────────────────────

    /// <summary>
    /// An admin reverted an AI provider to a previous model version via the rollback endpoint
    /// (US_069 AC-4, AIR-O05). Audit entry records provider, before/after model versions, reason.
    /// </summary>
    AiModelVersionRollback,

    // ── US_093 — HIPAA Technical Safeguards Verification (AC-1, NFR-041, NFR-042) ──

    /// <summary>
    /// Admin triggered a HIPAA technical safeguard verification run (US_093 AC-1).
    /// Audit entry records who triggered the run and the resulting ComplianceVerificationLog ID.
    /// </summary>
    ComplianceVerification,

    // ── US_093 task_003 — HIPAA Administrative Safeguards (AC-2, edge case 2) ─

    /// <summary>
    /// Admin created a new compliance policy version (US_093 AC-2).
    /// Audit entry records policy type, title, version, and creator.
    /// </summary>
    CompliancePolicyCreated,

    /// <summary>
    /// Compliance officer approved a compliance policy, transitioning it to Active (US_093 AC-2).
    /// </summary>
    CompliancePolicyApproved,

    /// <summary>
    /// Admin created or updated a compliance evaluation rule (US_093 AC-2, edge case 2).
    /// </summary>
    ComplianceRuleUpserted,

    /// <summary>
    /// Admin triggered a compliance rule evaluation run (US_093 AC-2, edge case 2).
    /// </summary>
    ComplianceRulesEvaluated,

    /// <summary>
    /// Admin ran the pre-migration PHI protection check (US_093 AC-4, DR-031).
    /// </summary>
    PhiMigrationPreCheck,

    /// <summary>
    /// Admin ran the post-migration PHI accessibility verification (US_093 AC-4, DR-031).
    /// </summary>
    PhiMigrationPostVerify,
}
