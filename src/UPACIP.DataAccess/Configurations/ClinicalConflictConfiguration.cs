using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ClinicalConflict"/> (US_044, AC-2, AC-3, AC-5, FR-053).
///
/// Maps to the <c>clinical_conflicts</c> table. Key constraints:
///   - <c>source_extracted_data_ids</c> and <c>source_document_ids</c> stored as JSONB arrays
///     via JSON value converter (List&lt;Guid&gt;) — no junction table required.
///   - Enum columns persisted as VARCHAR for forward-compatible extension.
///   - Composite index on (patient_id, status) accelerates active conflict queue lookups.
///   - Composite index on (is_urgent, created_at DESC) drives urgent review queue ordering (AC-3).
///   - Composite index on (patient_id, conflict_type) supports type-scoped conflict queries.
///   - <c>resolved_by_user_id</c> uses Restrict delete to preserve the resolution audit trail.
///   - <c>profile_version_id</c> uses SetNull to decouple conflict records if a version row
///     is ever removed without destroying the conflict history.
/// </summary>
public sealed class ClinicalConflictConfiguration : IEntityTypeConfiguration<ClinicalConflict>
{
    public void Configure(EntityTypeBuilder<ClinicalConflict> builder)
    {
        builder.ToTable("clinical_conflicts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.PatientId)
            .HasColumnName("patient_id")
            .IsRequired();

        builder.Property(c => c.ConflictType)
            .HasColumnName("conflict_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(c => c.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ConflictStatus.Detected);

        builder.Property(c => c.IsUrgent)
            .HasColumnName("is_urgent")
            .IsRequired()
            .HasDefaultValue(false);

        // source_extracted_data_ids: JSONB array of ExtractedData UUIDs.
        // Preserves AC-2 / AC-5 source citations at the extraction level without a junction table.
        builder.Property(c => c.SourceExtractedDataIds)
            .HasColumnName("source_extracted_data_ids")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>());

        // source_document_ids: JSONB array of ClinicalDocument UUIDs.
        // Preserves AC-2 / AC-3 source citations at the document level.
        builder.Property(c => c.SourceDocumentIds)
            .HasColumnName("source_document_ids")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>());

        builder.Property(c => c.ConflictDescription)
            .HasColumnName("conflict_description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.AiExplanation)
            .HasColumnName("ai_explanation")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.AiConfidenceScore)
            .HasColumnName("ai_confidence_score")
            .IsRequired();

        // resolution_type: VARCHAR enum — null while conflict is open.
        builder.Property(c => c.ResolutionType)
            .HasColumnName("resolution_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired(false);

        // selected_extracted_data_id: nullable FK to the chosen ExtractedData record (AC-2).
        builder.Property(c => c.SelectedExtractedDataId)
            .HasColumnName("selected_extracted_data_id")
            .IsRequired(false);

        // both_valid_explanation: text — populated only for BothValid resolution (EC-2).
        builder.Property(c => c.BothValidExplanation)
            .HasColumnName("both_valid_explanation")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(c => c.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id")
            .IsRequired(false);

        builder.Property(c => c.ResolutionNotes)
            .HasColumnName("resolution_notes")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(c => c.ResolvedAt)
            .HasColumnName("resolved_at")
            .IsRequired(false);

        builder.Property(c => c.ProfileVersionId)
            .HasColumnName("profile_version_id")
            .IsRequired(false);

        // BaseEntity audit columns — explicit column names for PostgreSQL snake_case convention.
        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // ── Indexes ───────────────────────────────────────────────────────────

        // Active conflict queue: retrieve all open conflicts for a patient ordered by status.
        builder.HasIndex(c => new { c.PatientId, c.Status })
            .HasDatabaseName("ix_clinical_conflicts_patient_id_status");

        // Urgent review queue: top-of-queue ordering for URGENT items (AC-3).
        // DESC ordering on created_at ensures newest urgent items surface first.
        builder.HasIndex(c => new { c.IsUrgent, c.CreatedAt })
            .HasDatabaseName("ix_clinical_conflicts_is_urgent_created_at");

        // Type-scoped conflict queries: retrieve all conflicts of a specific type for a patient.
        builder.HasIndex(c => new { c.PatientId, c.ConflictType })
            .HasDatabaseName("ix_clinical_conflicts_patient_id_conflict_type");

        // ── Foreign keys ──────────────────────────────────────────────────────

        // Cascade: removing a patient removes all their conflict records.
        builder.HasOne(c => c.Patient)
            .WithMany(p => p.ClinicalConflicts)
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: do not cascade-delete conflict resolution history when a staff user is removed.
        builder.HasOne(c => c.ResolvedByUser)
            .WithMany()
            .HasForeignKey(c => c.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // SetNull: if the referenced profile version is deleted, keep the conflict record
        // but clear the version link rather than cascading the delete.
        builder.HasOne(c => c.ProfileVersion)
            .WithMany()
            .HasForeignKey(c => c.ProfileVersionId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Restrict: do not cascade-delete the conflict record when the chosen extracted data
        // row is removed — the resolution audit trail must be preserved (AC-2).
        builder.HasOne(c => c.SelectedExtractedData)
            .WithMany()
            .HasForeignKey(c => c.SelectedExtractedDataId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
