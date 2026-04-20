using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class ClinicalDocumentConfiguration : IEntityTypeConfiguration<ClinicalDocument>
{
    public void Configure(EntityTypeBuilder<ClinicalDocument> builder)
    {
        builder.ToTable("clinical_documents");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.DocumentCategory)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.ProcessingStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.FilePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.HasIndex(c => c.PatientId)
            .HasDatabaseName("ix_clinical_documents_patient_id");

        builder.HasIndex(c => c.ProcessingStatus)
            .HasDatabaseName("ix_clinical_documents_processing_status");

        builder.HasOne(c => c.Patient)
            .WithMany(p => p.ClinicalDocuments)
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Uploader FK — restrict delete to prevent accidental user removal destroying document history.
        builder.HasOne(c => c.UploaderUser)
            .WithMany(u => u.UploadedDocuments)
            .HasForeignKey(c => c.UploaderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.ExtractedData)
            .WithOne(e => e.Document)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
