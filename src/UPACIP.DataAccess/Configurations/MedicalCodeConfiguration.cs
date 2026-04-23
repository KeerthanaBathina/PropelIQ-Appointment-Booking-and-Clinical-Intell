using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

public sealed class MedicalCodeConfiguration : IEntityTypeConfiguration<MedicalCode>
{
    public void Configure(EntityTypeBuilder<MedicalCode> builder)
    {
        builder.ToTable("medical_codes");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.CodeType)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(m => m.CodeValue).IsRequired().HasMaxLength(20);
        builder.Property(m => m.Description).IsRequired().HasMaxLength(500);
        builder.Property(m => m.Justification).IsRequired().HasMaxLength(1000);

        builder.Property(m => m.ApprovedByUserId).IsRequired(false);
        builder.Property(m => m.AiConfidenceScore).IsRequired(false);

        // ICD-10 library validation extensions (US_047 AC-3, AC-4) — all nullable for
        // backward compatibility with existing CPT codes and pre-versioning records.
        builder.Property(m => m.RelevanceRank).IsRequired(false);

        builder.Property(m => m.RevalidationStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(m => m.LibraryVersion)
            .HasMaxLength(20)
            .IsRequired(false);

        // CPT-specific bundle support (US_048 AC-3, task_003_db_cpt_code_library).
        // IsBundled defaults to false so existing ICD-10 rows are unaffected.
        builder.Property(m => m.IsBundled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.BundleGroupId)
            .IsRequired(false);

        // Composite index to speed up code-type lookups per patient and to enforce uniqueness.
        builder.HasIndex(m => new { m.PatientId, m.CodeType, m.CodeValue })
            .HasDatabaseName("ix_medical_codes_patient_codetype_codevalue");

        builder.HasOne(m => m.Patient)
            .WithMany(p => p.MedicalCodes)
            .HasForeignKey(m => m.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.ApprovedByUser)
            .WithMany()
            .HasForeignKey(m => m.ApprovedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ─────────────────────────────────────────────────────────────────────
        // Verification lifecycle fields (US_049 AC-2, AC-4, EC-1)
        // ─────────────────────────────────────────────────────────────────────

        // All existing rows (AI-suggested codes) start as Pending so staff must review them.
        builder.Property(m => m.VerificationStatus)
            .HasConversion<string>()
            .HasMaxLength(15)
            .IsRequired()
            .HasDefaultValue(CodeVerificationStatus.Pending);

        builder.Property(m => m.VerifiedByUserId).IsRequired(false);
        builder.Property(m => m.VerifiedAt).IsRequired(false);

        builder.Property(m => m.OverrideJustification)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(m => m.OriginalCodeValue)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(m => m.IsDeprecated)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne(m => m.VerifiedByUser)
            .WithMany()
            .HasForeignKey(m => m.VerifiedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Supports the verification queue — staff views all Pending codes for a patient.
        builder.HasIndex(m => new { m.PatientId, m.VerificationStatus })
            .HasDatabaseName("ix_medical_codes_patient_verification_status");

        // ─────────────────────────────────────────────────────────────────────
        // Payer rule validation fields (US_051 AC-1, AC-4, task_003_db)
        // ─────────────────────────────────────────────────────────────────────

        builder.Property(m => m.PayerValidationStatus)
            .HasConversion<string>()
            .HasMaxLength(15)
            .IsRequired()
            .HasDefaultValue(UPACIP.DataAccess.Enums.PayerValidationStatus.NotValidated);

        builder.Property(m => m.BundlingCheckResult)
            .HasConversion<string>()
            .HasMaxLength(15)
            .IsRequired()
            .HasDefaultValue(UPACIP.DataAccess.Enums.BundlingCheckResult.NotChecked);

        builder.Property(m => m.SequenceOrder)
            .IsRequired()
            .HasDefaultValue(0);
    }
}
