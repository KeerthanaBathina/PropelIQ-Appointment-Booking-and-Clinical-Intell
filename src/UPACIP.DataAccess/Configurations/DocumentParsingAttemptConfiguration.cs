using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core configuration for <see cref="DocumentParsingAttempt"/> (US_039 task_004, AC-4, EC-1, EC-2).
///
/// Key design decisions:
/// - AttemptId is the PK; rows are append-only (no UpdatedAt).
/// - document_parsing_attempts table uses snake_case per project convention.
/// - Index on (DocumentId, AttemptNumber) supports FIFO dispatch and due-retry queries (EC-2).
/// - Index on NextAttemptAt supports worker-restart resume: find all rows where NextAttemptAt ≤ now (EC-1).
/// - FailureReason capped at 1000 chars; AiProvider at 20 chars (no PII — AIR-S01).
/// </summary>
public sealed class DocumentParsingAttemptConfiguration : IEntityTypeConfiguration<DocumentParsingAttempt>
{
    public void Configure(EntityTypeBuilder<DocumentParsingAttempt> builder)
    {
        builder.ToTable("document_parsing_attempts");

        builder.HasKey(a => a.AttemptId);
        builder.Property(a => a.AttemptId).ValueGeneratedNever(); // App-generated UUID.

        builder.Property(a => a.AttemptNumber)
            .IsRequired();

        builder.Property(a => a.StartedAt)
            .IsRequired();

        builder.Property(a => a.CompletedAt);

        builder.Property(a => a.FailureCategory)
            .HasMaxLength(50);

        builder.Property(a => a.FailureReason)
            .HasMaxLength(1000);

        builder.Property(a => a.AiProvider)
            .HasMaxLength(20);

        builder.Property(a => a.ModelConfidence);

        builder.Property(a => a.NextAttemptAt);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // ── Relationships ─────────────────────────────────────────────────────
        builder.HasOne(a => a.Document)
            .WithMany(d => d.ParseAttempts)
            .HasForeignKey(a => a.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Primary lookup: all attempts for a given document ordered by attempt number.
        // Supports retry-count queries and FIFO dispatch visibility (EC-2).
        builder.HasIndex(a => new { a.DocumentId, a.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("ix_document_parsing_attempts_document_id_attempt_number");

        // Worker-restart resume: find rows with a pending next-attempt timestamp (EC-1).
        builder.HasIndex(a => a.NextAttemptAt)
            .HasDatabaseName("ix_document_parsing_attempts_next_attempt_at");

        // Recent-activity queries: staff dashboard can see parsing history sorted by start time.
        builder.HasIndex(a => a.StartedAt)
            .HasDatabaseName("ix_document_parsing_attempts_started_at");
    }
}
