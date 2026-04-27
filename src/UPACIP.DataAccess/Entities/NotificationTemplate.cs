namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persistent definition of a notification message template used by the notification
/// delivery pipeline (US_058 AC-3, FR-095).
///
/// <para>
/// A <see cref="NotificationTemplate"/> row defines the channel, trigger event, and
/// message body skeleton for a particular notification type (e.g. appointment reminder,
/// cancellation notice).  At send-time the <see cref="MessageBody"/> is resolved
/// by substituting patient- and appointment-specific tokens.
/// </para>
///
/// <para>
/// This entity is distinct from <c>NotificationLog</c>, which records individual
/// per-appointment delivery attempts.  Templates are read/written via the Admin
/// Configuration UI (SCR-015) and cached by <c>ConfigurationService</c>.
/// </para>
///
/// <para>Inherits <see cref="BaseEntity"/> — provides Id, CreatedAt, UpdatedAt.</para>
/// </summary>
public sealed class NotificationTemplate : BaseEntity
{
    /// <summary>
    /// Human-readable template name shown in the Admin UI (e.g. "Appointment Reminder").
    /// Max 200 characters, must be unique across all templates.
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>
    /// Delivery channel: <c>Email</c>, <c>SMS</c>, or <c>InApp</c>.
    /// Max 50 characters.
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// The domain event that triggers this template (e.g. <c>AppointmentBooked</c>,
    /// <c>AppointmentCancelled</c>, <c>Reminder24h</c>).
    /// Max 100 characters.
    /// </summary>
    public string TriggerEvent { get; set; } = string.Empty;

    /// <summary>
    /// Template message body with token placeholders (e.g. <c>{{PatientName}}</c>,
    /// <c>{{AppointmentDate}}</c>).
    /// Stored as PostgreSQL <c>text</c> (unlimited length).
    /// </summary>
    public string MessageBody { get; set; } = string.Empty;

    /// <summary>
    /// Whether this template is active and should be used for outgoing notifications.
    /// Inactive templates are retained for audit purposes but not dispatched.
    /// Default: <c>true</c>.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Email subject line — required for <c>Email</c> channel templates, null for SMS/InApp.
    /// Max 200 characters.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// JSON array of token names that the template body may reference
    /// (e.g. <c>["patient_name","date","time","provider"]</c>).
    /// Stored as PostgreSQL <c>jsonb</c>.  Null until explicitly set.
    /// </summary>
    public string? AllowedVariables { get; set; }

    /// <summary>
    /// Optimistic-concurrency token.  Incremented by the service layer on each save;
    /// EF Core includes it in every UPDATE WHERE clause (US_060 AC-2).
    /// Default: <c>0</c>.
    /// </summary>
    public int Version { get; set; } = 0;

    /// <summary>
    /// Identity of the admin user who last modified this template (US_060 AC-4).
    /// Null if the row was created by seed data or an automated process.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }
}
