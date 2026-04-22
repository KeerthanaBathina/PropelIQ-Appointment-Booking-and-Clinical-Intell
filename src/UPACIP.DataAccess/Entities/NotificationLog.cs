using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Records each notification attempt sent to a patient for an appointment.
/// NotificationLog does NOT extend <see cref="BaseEntity"/> because:
/// — it uses its own primary key name (<c>NotificationId</c>), and
/// — notification records have only a <c>CreatedAt</c> timestamp; they are not updated
///   in place (each retry creates a new row).
/// </summary>
public sealed class NotificationLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid NotificationId { get; set; } = Guid.NewGuid();

    /// <summary>FK to the <see cref="Appointment"/> this notification relates to.</summary>
    public Guid AppointmentId { get; set; }

    /// <summary>Notification event type (confirmation, reminder, slot-swap, etc.).</summary>
    public NotificationType NotificationType { get; set; }

    /// <summary>Channel over which the notification was dispatched.</summary>
    public DeliveryChannel DeliveryChannel { get; set; }

    /// <summary>Outcome of the latest delivery attempt.</summary>
    public NotificationStatus Status { get; set; }

    /// <summary>Number of delivery attempts made (initial attempt = 0, first retry = 1, etc.).</summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Normalised recipient address for this notification record — email address
    /// for <see cref="DeliveryChannel.Email"/>, E.164 phone number for
    /// <see cref="DeliveryChannel.Sms"/>.  Null for rows created before US_037.
    /// </summary>
    public string? RecipientAddress { get; set; }

    /// <summary>UTC timestamp when the notification was successfully sent. Null if not yet sent.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent delivery attempt (initial or retry).
    /// Updated after each attempt.  Null for rows created before US_037.
    /// </summary>
    public DateTime? FinalAttemptAt { get; set; }

    /// <summary>
    /// <c>true</c> when all orchestration retries have been exhausted and the notification
    /// is marked <see cref="NotificationStatus.PermanentlyFailed"/>.  Operations staff must
    /// manually follow up to deliver the message through an alternative channel (US_037 AC-3).
    /// </summary>
    public bool IsStaffReviewRequired { get; set; }

    /// <summary>
    /// <c>true</c> when a bounce outcome has flagged this notification record for patient
    /// contact-data validation (US_037 EC-2).  Linked to <see cref="Patient.ContactUpdateRequired"/>.
    /// </summary>
    public bool IsContactValidationRequired { get; set; }

    /// <summary>UTC timestamp when this log record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Appointment Appointment { get; set; } = null!;

    /// <summary>
    /// Per-attempt detail records for this notification (one row per send or retry).
    /// Empty for notification rows created before US_037.
    /// </summary>
    public ICollection<NotificationDeliveryAttempt> DeliveryAttempts { get; set; } = [];
}
