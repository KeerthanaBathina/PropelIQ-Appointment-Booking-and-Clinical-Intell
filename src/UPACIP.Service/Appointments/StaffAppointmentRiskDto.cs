using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Shared projection carrying no-show risk metadata for staff-facing appointment list
/// endpoints (SCR-010 staff schedule, SCR-011 arrival queue).
///
/// Consumed by:
///   - <c>GET /api/staff/appointments/today</c>  (StaffDashboardController)
///   - <c>GET /api/staff/queue</c>               (ArrivalQueueController)
///   - US_021 slot-swap candidate ranking        (PreferredSlotSwapService)
///
/// Design notes:
///   - All risk fields are nullable: appointments not yet scored return nulls and the
///     frontend renders "N/A" per task_001_fe_no_show_risk_display (AC-3).
///   - <see cref="IsEstimated"/> = true drives the "Est." label in the frontend badge
///     (AC-3, task_001_fe_no_show_risk_display).
///   - <see cref="RequiresOutreach"/> = true surfaces the outreach alert indicator (EC-2).
///   - No PII fields are included — only appointment-level and risk metadata (AIR-S01, NFR-017).
/// </summary>
public sealed record StaffAppointmentRiskDto
{
    /// <summary>Appointment UUID.</summary>
    public Guid AppointmentId { get; init; }

    /// <summary>Booking reference (e.g. BK-20260421-X7R2KP). Null for walk-ins or pre-US_018 records.</summary>
    public string? BookingReference { get; init; }

    /// <summary>UTC time of the appointment.</summary>
    public DateTime AppointmentTime { get; init; }

    /// <summary>Display name of the assigned provider.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Appointment category (e.g. "General Checkup").</summary>
    public string? AppointmentType { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>True when the appointment was created as a walk-in.</summary>
    public bool IsWalkIn { get; init; }

    // ── Risk metadata (nullable until first scoring run) ─────────────────────

    /// <summary>
    /// Persisted 0-100 risk score. Null if the appointment has not yet been scored.
    /// Matches the value stored in <c>appointments.no_show_risk_score</c>.
    /// </summary>
    public int? NoShowRiskScore { get; init; }

    /// <summary>
    /// Discrete risk band (Low / Medium / High). Null if not yet scored.
    /// Used for color-coding in SCR-010 and SCR-011 (AC-2).
    /// </summary>
    public NoShowRiskBand? RiskBand { get; init; }

    /// <summary>
    /// True when the score was produced by rule-based fallback (insufficient history — AC-3).
    /// Frontend renders an "Est." suffix on the badge when this is true.
    /// </summary>
    public bool? IsEstimated { get; init; }

    /// <summary>
    /// True when the score meets or exceeds the high-risk outreach threshold.
    /// Staff workflow uses this flag to decide whether to initiate proactive contact (EC-2).
    /// </summary>
    public bool? RequiresOutreach { get; init; }

    // ── Queue metadata (only populated for SCR-011 queue view) ───────────────

    /// <summary>Queue position within today's waiting list. Null for schedule view.</summary>
    public int? QueuePosition { get; init; }

    /// <summary>Computed wait time in minutes since arrival. Null when not in queue.</summary>
    public int? WaitMinutes { get; init; }
}
