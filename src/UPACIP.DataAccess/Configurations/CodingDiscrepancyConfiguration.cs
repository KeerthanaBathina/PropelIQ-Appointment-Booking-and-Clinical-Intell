using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class CodingDiscrepancyConfiguration : IEntityTypeConfiguration<CodingDiscrepancy>
{
    public void Configure(EntityTypeBuilder<CodingDiscrepancy> builder)
    {
        builder.ToTable("coding_discrepancies");

        builder.HasKey(d => d.DiscrepancyId);
        builder.Property(d => d.DiscrepancyId).ValueGeneratedOnAdd();

        // ─────────────────────────────────────────────────────────────────────
        // Code columns — max 20 chars matches MedicalCode.CodeValue bound
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(d => d.AiSuggestedCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(d => d.StaffSelectedCode)
            .IsRequired()
            .HasMaxLength(20);

        // ─────────────────────────────────────────────────────────────────────
        // Enum columns stored as strings for readability in BI/reporting tools
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(d => d.CodeType)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        // Max 16 chars: "PartialOverride" (15) + 1 headroom
        builder.Property(d => d.DiscrepancyType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(d => d.OverrideJustification)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(d => d.DetectedAt).IsRequired();
        builder.Property(d => d.CreatedAt).IsRequired();

        // ─────────────────────────────────────────────────────────────────────
        // Relationships
        // ─────────────────────────────────────────────────────────────────────

        // Restrict delete: preserve discrepancy records even if the medical code is
        // logically deleted via a status flag in the future.
        builder.HasOne(d => d.MedicalCode)
            .WithMany()
            .HasForeignKey(d => d.MedicalCodeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict delete: patient-level analytics must not be silently dropped if a
        // patient record is soft-deleted.
        builder.HasOne(d => d.Patient)
            .WithMany()
            .HasForeignKey(d => d.PatientId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // ─────────────────────────────────────────────────────────────────────
        // Indexes (US_050 FR-068 — patient-level and daily aggregation queries)
        // ─────────────────────────────────────────────────────────────────────

        // Patient-level discrepancy history ordered by recency.
        builder.HasIndex(d => new { d.PatientId, d.DetectedAt })
            .HasDatabaseName("ix_coding_discrepancies_patient_id_detected_at");

        // Medical-code lookup — joins to CodingAuditLog for cross-reference.
        builder.HasIndex(d => d.MedicalCodeId)
            .HasDatabaseName("ix_coding_discrepancies_medical_code_id");
    }
}
