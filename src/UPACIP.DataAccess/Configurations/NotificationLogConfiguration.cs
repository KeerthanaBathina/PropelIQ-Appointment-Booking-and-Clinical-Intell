using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs");

        // NotificationLog uses NotificationId as its primary key (not the BaseEntity Id convention).
        builder.HasKey(n => n.NotificationId);
        builder.Property(n => n.NotificationId).ValueGeneratedOnAdd();

        builder.Property(n => n.NotificationType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.DeliveryChannel)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired();

        builder.Property(n => n.SentAt).IsRequired(false);

        // New columns added in US_037 task_002 — nullable for backward compatibility
        builder.Property(n => n.RecipientAddress)
            .HasMaxLength(320)
            .IsRequired(false);

        builder.Property(n => n.FinalAttemptAt)
            .IsRequired(false);

        builder.Property(n => n.IsStaffReviewRequired)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(n => n.IsContactValidationRequired)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(n => n.AppointmentId)
            .HasDatabaseName("ix_notification_logs_appointment_id");

        // Admin queries: filter by final status (permanently_failed, failed, sent)
        builder.HasIndex(n => n.Status)
            .HasDatabaseName("ix_notification_logs_status");

        // Staff-review queue: efficiently surface all rows awaiting follow-up
        builder.HasIndex(n => n.IsStaffReviewRequired)
            .HasDatabaseName("ix_notification_logs_staff_review_required");

        // Time-range analytics and ordered buffer flushing (EC-1)
        builder.HasIndex(n => n.CreatedAt)
            .HasDatabaseName("ix_notification_logs_created_at");

        // The inverse side is configured in AppointmentConfiguration.
        builder.HasOne(n => n.Appointment)
            .WithMany(a => a.Notifications)
            .HasForeignKey(n => n.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
