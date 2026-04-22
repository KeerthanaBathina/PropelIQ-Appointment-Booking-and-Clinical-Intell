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

        builder.Property(c => c.OriginalFileName)
            .IsRequired()
            .HasMaxLength(260);

        // ContentType and FileSizeBytes are nullable for backward compat with pre-US_038 rows.
        builder.Property(c => c.ContentType)
            .HasMaxLength(127);

        builder.Property(c => c.FileSizeBytes);

        builder.Property(c => c.FilePath)
            .IsRequired()
            .HasMaxLength(1000);

        // Composite index: patient document list filtered by status (US_038 EC-2 — supports
        // later uploaded → queued transition queries without a separate status-only scan).
        builder.HasIndex(c => new { c.PatientId, c.ProcessingStatus })
            .HasDatabaseName("ix_clinical_documents_patient_id_status");

        // Temporal index for recent document retrieval and upload audit sweeps.
        builder.HasIndex(c => c.UploadDate)
            .HasDatabaseName("ix_clinical_documents_upload_date");

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

        // Parsing lifecycle metadata (US_039 task_004, AC-4, AC-5, EC-1).
        builder.Property(c => c.ParseAttemptCount);
        builder.Property(c => c.ParseStartedAt);
        builder.Property(c => c.ParseCompletedAt);
        builder.Property(c => c.ParseNextAttemptAt);
        builder.Property(c => c.RequiresManualReview)
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(c => c.ManualReviewReason)
            .HasMaxLength(500);

        // Extraction outcome (US_040 task_003, EC-1, EC-2): distinguishes no-data-extracted and
        // unsupported-language from parser failures without overloading ProcessingStatus.
        builder.Property(c => c.ExtractionOutcome)
            .HasMaxLength(30);

        // Retry-resume index: dispatcher finds documents with a pending next-attempt time (EC-1).
        builder.HasIndex(c => c.ParseNextAttemptAt)
            .HasDatabaseName("ix_clinical_documents_parse_next_attempt_at");

        // Manual-review dashboard query: quickly list documents requiring staff follow-up (AC-5).
        builder.HasIndex(c => new { c.RequiresManualReview, c.PatientId })
            .HasDatabaseName("ix_clinical_documents_requires_manual_review_patient_id");

        // Extraction-outcome index: review workflows filter by outcome status (EC-1, EC-2).
        builder.HasIndex(c => c.ExtractionOutcome)
            .HasDatabaseName("ix_clinical_documents_extraction_outcome");

        builder.HasMany(c => c.ParseAttempts)
            .WithOne(a => a.Document)
            .HasForeignKey(a => a.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Document version lineage (US_042 task_004 AC-2, AC-3, EC-1).
        builder.Property(c => c.VersionNumber)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(c => c.PreviousVersionId)
            .IsRequired(false);

        builder.Property(c => c.IsSuperseded)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.SupersededAtUtc)
            .IsRequired(false);

        builder.Property(c => c.ReconsolidationNeeded)
            .IsRequired()
            .HasDefaultValue(false);

        // Self-referencing FK: replacement document → previous version.
        builder.HasOne(c => c.PreviousVersion)
            .WithMany(c => c.ReplacementVersions)
            .HasForeignKey(c => c.PreviousVersionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Active-version query index: find current non-superseded documents for a patient.
        builder.HasIndex(c => new { c.PatientId, c.IsSuperseded })
            .HasDatabaseName("ix_clinical_documents_patient_id_is_superseded");

        // Reconsolidation work-queue index: EP-007 scans for documents needing reconsolidation.
        builder.HasIndex(c => c.ReconsolidationNeeded)
            .HasDatabaseName("ix_clinical_documents_reconsolidation_needed");
    }
}
