using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core table mapping, index, and relationship configuration for
/// <see cref="NotificationDeliveryAttempt"/> (US_037 AC-1, AC-4).
/// </summary>
public sealed class NotificationDeliveryAttemptConfiguration
    : IEntityTypeConfiguration<NotificationDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttempt> builder)
    {
        builder.ToTable("notification_delivery_attempts");

        builder.HasKey(a => a.AttemptId);
        builder.Property(a => a.AttemptId).ValueGeneratedOnAdd();

        builder.Property(a => a.NotificationId).IsRequired();
        builder.Property(a => a.AppointmentId).IsRequired();

        builder.Property(a => a.Channel)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(a => a.RecipientAddress)
            .HasMaxLength(320)  // RFC 5321 max email address length
            .IsRequired();

        builder.Property(a => a.AttemptNumber).IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired();

        builder.Property(a => a.ProviderName)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(a => a.AttemptedAt).IsRequired();
        builder.Property(a => a.DurationMs).IsRequired(false);

        builder.Property(a => a.FailureReason)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(a => a.CreatedAt).IsRequired();

        // ── Indexes ──────────────────────────────────────────────────────────

        // FK index — join to parent NotificationLog
        builder.HasIndex(a => a.NotificationId)
            .HasDatabaseName("ix_notification_delivery_attempts_notification_id");

        // Admin queries: filter by appointment
        builder.HasIndex(a => a.AppointmentId)
            .HasDatabaseName("ix_notification_delivery_attempts_appointment_id");

        // Failed-delivery scans: find retryable items by status + time
        builder.HasIndex(a => new { a.Status, a.AttemptedAt })
            .HasDatabaseName("ix_notification_delivery_attempts_status_attempted_at");

        // Statistics: filter by channel for channel-specific success/failure rates
        builder.HasIndex(a => a.Channel)
            .HasDatabaseName("ix_notification_delivery_attempts_channel");

        // ── Relationships ─────────────────────────────────────────────────────

        builder.HasOne(a => a.NotificationLog)
            .WithMany(n => n.DeliveryAttempts)
            .HasForeignKey(a => a.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
