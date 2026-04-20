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
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(n => n.SentAt).IsRequired(false);

        builder.HasIndex(n => n.AppointmentId)
            .HasDatabaseName("ix_notification_logs_appointment_id");

        // The inverse side is configured in AppointmentConfiguration.
        builder.HasOne(n => n.Appointment)
            .WithMany(a => a.Notifications)
            .HasForeignKey(n => n.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
