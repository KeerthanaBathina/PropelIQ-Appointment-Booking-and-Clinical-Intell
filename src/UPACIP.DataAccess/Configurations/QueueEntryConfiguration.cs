using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();

        builder.Property(q => q.Priority)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(q => q.Status)
            .HasConversion<string>()
            .HasMaxLength(15)
            .IsRequired();

        // One appointment can have at most one queue entry — enforce at the database level.
        builder.HasIndex(q => q.AppointmentId)
            .IsUnique()
            .HasDatabaseName("ix_queue_entries_appointment_id");

        // The inverse side (Appointment → QueueEntry) is configured in AppointmentConfiguration.
        builder.HasOne(q => q.Appointment)
            .WithOne(a => a.QueueEntry)
            .HasForeignKey<QueueEntry>(q => q.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
