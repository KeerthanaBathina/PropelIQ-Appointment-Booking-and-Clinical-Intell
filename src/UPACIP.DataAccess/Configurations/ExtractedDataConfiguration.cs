using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

public sealed class ExtractedDataConfiguration : IEntityTypeConfiguration<ExtractedData>
{
    public void Configure(EntityTypeBuilder<ExtractedData> builder)
    {
        builder.ToTable("extracted_data");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.DataType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // JSONB column for the structured AI extraction result.
        // Explicitly configure the Metadata Dictionary property inside the owned type to
        // use 'jsonb' column type, preventing Npgsql from defaulting it to 'hstore'
        // (which would require the hstore extension and pollutes migrations).
        builder.OwnsOne(e => e.DataContent, owned =>
        {
            owned.ToJson();
            owned.Property(d => d.Metadata).HasColumnType("jsonb");
        });

        builder.Property(e => e.ConfidenceScore).IsRequired();
        builder.Property(e => e.SourceAttribution).IsRequired().HasMaxLength(200);

        // Structured attribution columns (US_040 task_003, AC-5).
        builder.Property(e => e.PageNumber)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.ExtractionRegion)
            .IsRequired()
            .HasDefaultValue(string.Empty)
            .HasMaxLength(200);

        builder.Property(e => e.VerifiedByUserId).IsRequired(false);

        // Verification tracking columns (US_041 AC-4, EC-1, EC-2).
        builder.Property(e => e.VerifiedAtUtc).IsRequired(false);

        builder.Property(e => e.VerificationStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(VerificationStatus.Pending);

        builder.Property(e => e.ReviewReason)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(ReviewReason.None);

        builder.HasIndex(e => e.DocumentId)
            .HasDatabaseName("ix_extracted_data_document_id");

        // Composite index: group extracted rows by document + category for profile assembly (US_040 task_003).
        builder.HasIndex(e => new { e.DocumentId, e.DataType })
            .HasDatabaseName("ix_extracted_data_document_id_data_type");

        // Temporal retrieval index: audit sweeps and time-ordered extractions.
        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("ix_extracted_data_created_at");

        // Review work-queue indexes (US_041 EC-2 — efficient flagged-row retrieval).
        builder.HasIndex(e => e.FlaggedForReview)
            .HasDatabaseName("ix_extracted_data_flagged_for_review");

        builder.HasIndex(e => e.VerificationStatus)
            .HasDatabaseName("ix_extracted_data_verification_status");

        builder.HasOne(e => e.Document)
            .WithMany(c => c.ExtractedData)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.VerifiedByUser)
            .WithMany()
            .HasForeignKey(e => e.VerifiedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Manual fallback / date-validation columns (US_046 task_002 AC-2, AC-3, edge case).
        builder.Property(e => e.IsIncompleteDate)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.DateConflictExplanation)
            .IsRequired(false)
            .HasMaxLength(1000);

        builder.HasIndex(e => e.IsIncompleteDate)
            .HasDatabaseName("ix_extracted_data_is_incomplete_date");

        // Archival support (US_042 task_004 AC-3, EC-1).
        builder.Property(e => e.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ArchivedAtUtc)
            .IsRequired(false);

        // Active-data query index: active (non-archived) rows by document, used by preview and review.
        builder.HasIndex(e => new { e.DocumentId, e.IsArchived })
            .HasDatabaseName("ix_extracted_data_document_id_is_archived");
    }
}
