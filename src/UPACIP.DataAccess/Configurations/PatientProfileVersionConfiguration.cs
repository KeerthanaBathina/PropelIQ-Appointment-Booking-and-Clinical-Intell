using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core configuration for <see cref="PatientProfileVersion"/> (US_043, AC-2, FR-056).
///
/// Maps to the <c>patient_profile_versions</c> table. Key constraints:
///   - Unique composite index on (patient_id, version_number) ensures version ordering
///     is deterministic and no duplicates can be inserted concurrently.
///   - Descending index on (patient_id, created_at DESC) accelerates latest-version
///     queries without a full table scan.
///   - <c>source_document_ids</c> stored as JSONB via JSON value converter (List&lt;Guid&gt;).
///   - <c>data_snapshot</c> stored as raw JSONB string — callers serialize the delta
///     before writing and deserialize after reading.
///   - <c>consolidated_by_user_id</c> uses Restrict delete to prevent accidental user
///     removal destroying the consolidation audit trail.
/// </summary>
public sealed class PatientProfileVersionConfiguration : IEntityTypeConfiguration<PatientProfileVersion>
{
    public void Configure(EntityTypeBuilder<PatientProfileVersion> builder)
    {
        builder.ToTable("patient_profile_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        builder.Property(v => v.PatientId)
            .HasColumnName("patient_id")
            .IsRequired();

        builder.Property(v => v.VersionNumber)
            .HasColumnName("version_number")
            .IsRequired();

        builder.Property(v => v.ConsolidatedByUserId)
            .HasColumnName("consolidated_by_user_id")
            .IsRequired(false);

        builder.Property(v => v.ConsolidationType)
            .HasColumnName("consolidation_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // source_document_ids: JSONB array of UUIDs — serialized via System.Text.Json value converter.
        // This avoids a separate junction table while retaining full queryability via PostgreSQL JSONB ops.
        builder.Property(v => v.SourceDocumentIds)
            .HasColumnName("source_document_ids")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>());

        // data_snapshot: free-form JSONB delta string — nullable (NULL for initial versions).
        builder.Property(v => v.DataSnapshot)
            .HasColumnName("data_snapshot")
            .HasColumnType("jsonb")
            .IsRequired(false);

        // ── Verification lifecycle fields (US_045, AC-4, FR-054) ──────────────

        // verification_status: VARCHAR enum — defaults to Unverified for all new versions.
        builder.Property(v => v.VerificationStatus)
            .HasColumnName("verification_status")
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired()
            .HasDefaultValue(ProfileVerificationStatus.Unverified);

        builder.Property(v => v.VerifiedByUserId)
            .HasColumnName("verified_by_user_id")
            .IsRequired(false);

        // verified_at: nullable UTC timestamp of final verification (AC-4).
        builder.Property(v => v.VerifiedAt)
            .HasColumnName("verified_at")
            .IsRequired(false);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Unique constraint: a patient cannot have two entries with the same version number.
        // Primary key for ordered version retrieval (AC-2).
        builder.HasIndex(v => new { v.PatientId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("uq_patient_profile_versions_patient_version");

        // Latest-version lookup: staff dashboard and consolidation service fetch the most
        // recent version for a patient with ORDER BY created_at DESC LIMIT 1.
        builder.HasIndex(v => new { v.PatientId, v.CreatedAt })
            .HasDatabaseName("ix_patient_profile_versions_patient_created_at");

        // Audit sweep: retrieve all versions by consolidating user (attribution queries).
        builder.HasIndex(v => v.ConsolidatedByUserId)
            .HasFilter("consolidated_by_user_id IS NOT NULL")
            .HasDatabaseName("ix_patient_profile_versions_consolidated_by_user_id");

        // Verification status lookup: fetch all versions awaiting or completing staff review (AC-4).
        builder.HasIndex(v => new { v.PatientId, v.VerificationStatus })
            .HasDatabaseName("ix_patient_profile_versions_patient_id_verification_status");

        // ── Foreign keys ──────────────────────────────────────────────────────

        builder.HasOne(v => v.Patient)
            .WithMany(p => p.ProfileVersions)
            .HasForeignKey(v => v.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: do not cascade-delete profile history when a staff user is removed.
        builder.HasOne(v => v.ConsolidatedByUser)
            .WithMany()
            .HasForeignKey(v => v.ConsolidatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Restrict: do not cascade-delete profile versions when the verifying staff user
        // is removed — the verification audit trail must be preserved (AC-4).
        builder.HasOne(v => v.VerifiedByUser)
            .WithMany()
            .HasForeignKey(v => v.VerifiedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
