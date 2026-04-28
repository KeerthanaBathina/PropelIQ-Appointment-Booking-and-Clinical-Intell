using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="ArchivedAppointmentReference"/>
/// (US_086 AC-3, AC-5, DR-018, DR-020).
///
/// Table: <c>archived_appointment_references</c> (default schema).
/// The reference row lives in the main schema alongside <c>appointments</c> so that
/// standard patient-history queries can join on it without a cross-schema lookup.
/// Full appointment details are in <c>archive.appointments</c> (archive schema, raw SQL only).
/// </summary>
public sealed class ArchivedAppointmentReferenceConfiguration
    : IEntityTypeConfiguration<ArchivedAppointmentReference>
{
    public void Configure(EntityTypeBuilder<ArchivedAppointmentReference> builder)
    {
        builder.ToTable("archived_appointment_references");

        // PK is the original appointment ID — ValueGeneratedNever because the application
        // layer sets it; no DB-side auto-generation required or desired.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.ArchiveTable)
            .HasMaxLength(100)
            .IsRequired();

        // Index on PatientId supports the primary query pattern: "get all archived references
        // for a patient" used by the patient-history timeline endpoint.
        builder.HasIndex(r => r.PatientId)
            .HasDatabaseName("ix_archived_appointment_references_patient_id");

        // FK to patients — Restrict (not Cascade) because the reference record's purpose is
        // to outlive the appointment row. Cascade would defeat the point of the reference.
        builder.HasOne(r => r.Patient)
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
