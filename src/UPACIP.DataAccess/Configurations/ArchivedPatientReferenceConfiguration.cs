using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="ArchivedPatientReference"/>
/// (US_087 AC-4, DR-021).
///
/// Table: <c>archived_patient_references</c> (default schema).
/// The stub lives in the main schema so audit-log resolution queries do not need
/// a cross-schema round-trip.  Full patient records are in <c>archive.patients</c>
/// (archive schema, accessed via raw SQL only).
/// </summary>
public sealed class ArchivedPatientReferenceConfiguration
    : IEntityTypeConfiguration<ArchivedPatientReference>
{
    public void Configure(EntityTypeBuilder<ArchivedPatientReference> builder)
    {
        builder.ToTable("archived_patient_references");

        // PK is the original patient ID — ValueGeneratedNever: set by the application.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.ArchiveSchema)
            .HasMaxLength(50)
            .IsRequired();

        // No FK back to patients — the original patient row no longer exists.
    }
}
