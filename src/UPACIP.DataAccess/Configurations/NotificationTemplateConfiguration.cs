using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="NotificationTemplate"/> (US_058 AC-3, FR-095).
///
/// Table: <c>notification_templates</c>
///
/// Constraints:
///   - Unique on <c>template_name</c>: prevents duplicate template names in the Admin UI.
///
/// Indexes:
///   - <c>ix_notification_templates_template_name</c>: O(1) lookup by name (Admin config load).
///   - <c>ix_notification_templates_channel_trigger</c>: fast lookup by channel + trigger
///     used by the notification dispatch pipeline to find the active template to use.
///
/// Seed data:
///   - Three default templates (Appointment Reminder, Cancellation Notice, No-Show Alert).
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>; no manual registration.
/// </summary>
public sealed class NotificationTemplateConfiguration
    : IEntityTypeConfiguration<NotificationTemplate>
{
    // ── Stable seed GUIDs — do NOT change after first migration ──────────────
    internal static readonly Guid AppointmentReminderEmailId =
        new("a1b2c3d4-e5f6-7890-abcd-000000000001");

    internal static readonly Guid CancellationNoticeSmsId =
        new("b2c3d4e5-f6a7-8901-bcde-000000000002");

    internal static readonly Guid NoShowAlertEmailId =
        new("c3d4e5f6-a7b8-9012-cdef-000000000003");

    // ── US_060 seed GUIDs ─────────────────────────────────────────────────────
    internal static readonly Guid BookingConfirmationEmailId =
        new("d4e5f6a7-b8c9-0123-def0-000000000004");

    internal static readonly Guid BookingConfirmationSmsId =
        new("e5f6a7b8-c9d0-1234-ef01-000000000005");

    internal static readonly Guid Reminder24hEmailId =
        new("f6a7b8c9-d0e1-2345-f012-000000000006");

    internal static readonly Guid Reminder24hSmsId =
        new("a7b8c9d0-e1f2-3456-0123-000000000007");

    internal static readonly Guid Reminder2hEmailId =
        new("b8c9d0e1-f2a3-4567-1234-000000000008");

    internal static readonly Guid Reminder2hSmsId =
        new("c9d0e1f2-a3b4-5678-2345-000000000009");

    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        // ── String columns ─────────────────────────────────────────────────────
        builder.Property(t => t.TemplateName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Channel)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.TriggerEvent)
            .HasMaxLength(100)
            .IsRequired();

        // MessageBody is unbounded — stored as PostgreSQL text
        builder.Property(t => t.MessageBody)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // ── US_060 columns ────────────────────────────────────────────────────
        builder.Property(t => t.Subject)
            .HasMaxLength(200)
            .IsRequired(false);

        // AllowedVariables is a JSON array stored as PostgreSQL jsonb
        builder.Property(t => t.AllowedVariables)
            .HasColumnType("jsonb")
            .IsRequired(false);

        // Optimistic-concurrency token — same pattern as SlotTemplate (US_059)
        builder.Property(t => t.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        builder.Property(t => t.UpdatedByUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        // ── Unique index on template_name ─────────────────────────────────────
        builder.HasIndex(t => t.TemplateName)
            .IsUnique()
            .HasDatabaseName("ix_notification_templates_template_name");

        // ── Composite index on channel + trigger_event ────────────────────────
        // Used by the notification dispatch pipeline to find the active template
        // for a given channel/trigger combination in O(log n).
        builder.HasIndex(t => new { t.Channel, t.TriggerEvent })
            .HasDatabaseName("ix_notification_templates_channel_trigger");

        // ── Seed default notification templates ───────────────────────────────
        builder.HasData(
            // ── US_058 seeds (updated with US_060 nullable columns) ────────────
            new NotificationTemplate
            {
                Id                = AppointmentReminderEmailId,
                TemplateName      = "Appointment Reminder",
                Channel           = "Email",
                TriggerEvent      = "Reminder24h",
                MessageBody       =
                    "Dear {{PatientName}},\n\n" +
                    "This is a reminder of your appointment on {{AppointmentDate}} at {{AppointmentTime}} " +
                    "with {{ProviderName}}.\n\n" +
                    "If you need to reschedule or cancel, please contact us at least 24 hours in advance.\n\n" +
                    "Thank you,\nThe Care Team",
                IsActive          = true,
                Subject           = null,
                AllowedVariables  = null,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            new NotificationTemplate
            {
                Id                = CancellationNoticeSmsId,
                TemplateName      = "Cancellation Notice",
                Channel           = "SMS",
                TriggerEvent      = "AppointmentCancelled",
                MessageBody       =
                    "Hi {{PatientName}}, your appointment on {{AppointmentDate}} at {{AppointmentTime}} " +
                    "has been cancelled. Call us to reschedule.",
                IsActive          = true,
                Subject           = null,
                AllowedVariables  = null,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            new NotificationTemplate
            {
                Id                = NoShowAlertEmailId,
                TemplateName      = "No-Show Alert",
                Channel           = "Email",
                TriggerEvent      = "PatientNoShow",
                MessageBody       =
                    "Dear {{PatientName}},\n\n" +
                    "We noticed you missed your appointment on {{AppointmentDate}} at {{AppointmentTime}}.\n\n" +
                    "Please contact us to schedule a new appointment at your earliest convenience.\n\n" +
                    "Thank you,\nThe Care Team",
                IsActive          = true,
                Subject           = null,
                AllowedVariables  = null,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            // ── US_060 seeds: Booking Confirmation ───────────────────────────
            new NotificationTemplate
            {
                Id                = BookingConfirmationEmailId,
                TemplateName      = "Booking Confirmation (Email)",
                Channel           = "Email",
                TriggerEvent      = "AppointmentBooked",
                Subject           = "Appointment Confirmed",
                MessageBody       =
                    "Dear {{patient_name}},\n\n" +
                    "Your appointment has been confirmed for {{date}} at {{time}} " +
                    "with {{provider}}.\n\n" +
                    "Please arrive 10 minutes early. If you need to reschedule, " +
                    "contact us at least 24 hours in advance.\n\n" +
                    "Thank you,\nThe Care Team",
                AllowedVariables  = "[\"patient_name\",\"date\",\"time\",\"provider\"]",
                IsActive          = true,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            new NotificationTemplate
            {
                Id                = BookingConfirmationSmsId,
                TemplateName      = "Booking Confirmation (SMS)",
                Channel           = "SMS",
                TriggerEvent      = "AppointmentBooked",
                Subject           = null,
                MessageBody       =
                    "Hi {{patient_name}}, your appointment is confirmed for {{date}} at {{time}}. " +
                    "Reply CANCEL to cancel.",
                AllowedVariables  = "[\"patient_name\",\"date\",\"time\"]",
                IsActive          = true,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            // ── US_060 seeds: 24h Reminder ────────────────────────────────────
            new NotificationTemplate
            {
                Id                = Reminder24hEmailId,
                TemplateName      = "24h Reminder (Email)",
                Channel           = "Email",
                TriggerEvent      = "Reminder24h",
                Subject           = "Appointment Reminder — Tomorrow",
                MessageBody       =
                    "Dear {{patient_name}},\n\n" +
                    "This is a reminder that you have an appointment tomorrow, {{date}}, at {{time}} " +
                    "with {{provider}}.\n\n" +
                    "If you need to cancel, please let us know at least 24 hours in advance.\n\n" +
                    "Thank you,\nThe Care Team",
                AllowedVariables  = "[\"patient_name\",\"date\",\"time\",\"provider\"]",
                IsActive          = true,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            new NotificationTemplate
            {
                Id                = Reminder24hSmsId,
                TemplateName      = "24h Reminder (SMS)",
                Channel           = "SMS",
                TriggerEvent      = "Reminder24h",
                Subject           = null,
                MessageBody       =
                    "Reminder: {{patient_name}}, you have an appointment tomorrow {{date}} at {{time}}. " +
                    "Reply CANCEL to cancel.",
                AllowedVariables  = "[\"patient_name\",\"date\",\"time\"]",
                IsActive          = true,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            // ── US_060 seeds: 2h Reminder ─────────────────────────────────────
            new NotificationTemplate
            {
                Id                = Reminder2hEmailId,
                TemplateName      = "2h Reminder (Email)",
                Channel           = "Email",
                TriggerEvent      = "Reminder2h",
                Subject           = "Appointment in 2 Hours",
                MessageBody       =
                    "Dear {{patient_name}},\n\n" +
                    "Your appointment with {{provider}} is in 2 hours at {{time}} today, {{date}}.\n\n" +
                    "Please make sure you arrive on time.\n\n" +
                    "Thank you,\nThe Care Team",
                AllowedVariables  = "[\"patient_name\",\"date\",\"time\",\"provider\"]",
                IsActive          = true,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            },
            new NotificationTemplate
            {
                Id                = Reminder2hSmsId,
                TemplateName      = "2h Reminder (SMS)",
                Channel           = "SMS",
                TriggerEvent      = "Reminder2h",
                Subject           = null,
                MessageBody       =
                    "{{patient_name}}, your appointment with {{provider}} is in 2 hours at {{time}}. " +
                    "See you soon!",
                AllowedVariables  = "[\"patient_name\",\"time\",\"provider\"]",
                IsActive          = true,
                Version           = 0,
                UpdatedByUserId   = null,
                CreatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt         = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
            });
    }
}
