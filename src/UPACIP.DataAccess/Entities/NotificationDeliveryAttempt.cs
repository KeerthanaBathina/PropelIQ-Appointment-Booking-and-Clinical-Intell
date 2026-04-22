using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Stores one ordered record for every concrete send attempt (initial or orchestration retry)
/// made for a <see cref="NotificationLog"/> entry (US_037 AC-1, AC-4).
///
/// This table enables per-attempt audit, admin statistics (success rate, average delivery
/// duration), and ordered buffer flushing on persistence recovery (EC-1).
///
/// Design notes:
/// <list type="bullet">
///   <item>Each initial send and each orchestration-level retry creates exactly one row here.</item>
///   <item>
///     <see cref="AttemptNumber"/> is 0-based: 0 = initial send, 1 = first retry (1 min),
///     2 = second retry (5 min), 3 = third retry (15 min / final).
///   </item>
///   <item>
///     Rows are append-only.  Outcomes are never updated in place so the full history
///     survives for audit purposes even after a notification is marked permanently failed.
///   </item>
/// </list>
/// </summary>
public sealed class NotificationDeliveryAttempt
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid AttemptId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to the parent <see cref="NotificationLog"/> row that this attempt belongs to.
    /// </summary>
    public Guid NotificationId { get; set; }

    /// <summary>
    /// Denormalised FK to the <see cref="Appointment"/> for efficient admin queries
    /// without joining through <see cref="NotificationLog"/>.
    /// </summary>
    public Guid AppointmentId { get; set; }

    /// <summary>Channel used for this specific attempt.</summary>
    public DeliveryChannel Channel { get; set; }

    /// <summary>
    /// Normalised recipient address for this attempt — email address for
    /// <see cref="DeliveryChannel.Email"/>, E.164 phone number for
    /// <see cref="DeliveryChannel.Sms"/>.
    /// </summary>
    public string RecipientAddress { get; set; } = string.Empty;

    /// <summary>
    /// 0-based attempt sequence.  0 = initial send; 1, 2, 3 = orchestration retries.
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>Outcome of this specific send attempt.</summary>
    public NotificationStatus Status { get; set; }

    /// <summary>
    /// Display name of the provider that handled this attempt (e.g. "SendGrid", "Gmail SMTP",
    /// "Twilio").  Null when the attempt failed before reaching a transport.
    /// </summary>
    public string? ProviderName { get; set; }

    /// <summary>UTC timestamp when this attempt was initiated.</summary>
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Round-trip delivery duration in milliseconds.  Null when the outcome was not
    /// a transport call (e.g. template-render failure, opt-out check).
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Error or failure description from the transport layer.
    /// Null when the attempt succeeded or was not attempted.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>UTC timestamp when this record was created (for ordered buffer flushing, EC-1).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public NotificationLog NotificationLog { get; set; } = null!;
}
