using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

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
    }
}
