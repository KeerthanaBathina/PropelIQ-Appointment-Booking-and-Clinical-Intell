using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        // Enum stored as string to keep data human-readable in PostgreSQL.
        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Optimistic-concurrency token — EF Core includes Version in every UPDATE WHERE clause.
        // If the row has been modified since the entity was loaded, DbUpdateConcurrencyException is thrown.
        builder.Property(a => a.Version)
            .IsConcurrencyToken();

        // JSONB column — EF Core 7+ ToJson() serializes the owned type as a single JSONB cell.
        builder.OwnsOne(a => a.PreferredSlotCriteria, owned =>
        {
            owned.ToJson();
        });

        builder.HasIndex(a => a.PatientId)
            .HasDatabaseName("ix_appointments_patient_id");

        builder.HasIndex(a => a.AppointmentTime)
            .HasDatabaseName("ix_appointments_appointment_time");

        // Composite unique constraint (DR-014): prevents duplicate bookings for the same
        // patient at the same time slot. PostgreSQL will reject any INSERT/UPDATE that
        // duplicates (patient_id, appointment_time) with SqlState = "23505" (unique_violation).
        // EF Core surfaces this as DbUpdateException containing PostgresException.
        builder.HasIndex(a => new { a.PatientId, a.AppointmentTime })
            .IsUnique()
            .HasDatabaseName("ix_appointments_patient_id_appointment_time");

        // FK is configured on PatientConfiguration (one-to-many); only navigation registered here.
        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.QueueEntry)
            .WithOne(q => q.Appointment)
            .HasForeignKey<QueueEntry>(q => q.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Notifications)
            .WithOne(n => n.Appointment)
            .HasForeignKey(n => n.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
