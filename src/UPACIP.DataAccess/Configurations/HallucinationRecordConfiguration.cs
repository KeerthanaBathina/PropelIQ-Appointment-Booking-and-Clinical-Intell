using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="HallucinationRecord"/> (US_074 task_002).
///
/// <para>Table: <c>hallucination_records</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Extends <see cref="BaseEntity"/> — uses standard <c>Id</c>, <c>CreatedAt</c>,
///   <c>UpdatedAt</c> pattern.</item>
///   <item><c>SourceSupportStatus</c> stored as <c>character varying(20)</c> string for
///   readability in raw SQL queries.</item>
///   <item>FK to <c>MedicalCode</c> with <c>DeleteBehavior.Restrict</c> — prevents
///   accidental deletion of medical codes that have associated verification records.</item>
///   <item>FK to <c>ApplicationUser</c> (<c>VerifiedByUserId</c>) with
///   <c>DeleteBehavior.Restrict</c>.</item>
///   <item>Composite index on (<c>MedicalCodeId</c>, <c>CreatedAt</c>) — primary access
///   path for "all verifications for a given code" queries.</item>
///   <item>Index on <c>SourceSupportStatus</c> — accelerates daily rate aggregation queries
///   that filter on <c>Unsupported</c>.</item>
///   <item>Index on <c>VerifiedAt</c> — supports date-range queries in the aggregation job.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class HallucinationRecordConfiguration : IEntityTypeConfiguration<HallucinationRecord>
{
    public void Configure(EntityTypeBuilder<HallucinationRecord> builder)
    {
        builder.ToTable("hallucination_records");

        builder.HasKey(r => r.Id);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.VerifiedAt).HasColumnType("timestamptz").IsRequired();

        // ── FKs ───────────────────────────────────────────────────────────────
        builder.Property(r => r.MedicalCodeId).IsRequired();
        builder.Property(r => r.VerifiedByUserId).IsRequired();

        // ── SourceSupportStatus — stored as string ────────────────────────────
        builder.Property(r => r.SourceSupportStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ── VerificationNotes — optional, capped at 1000 chars ────────────────
        builder.Property(r => r.VerificationNotes)
            .HasMaxLength(1000)
            .IsRequired(false);

        // ── IsRetroactive ─────────────────────────────────────────────────────
        builder.Property(r => r.IsRetroactive).IsRequired();

        // ── FK: MedicalCode ───────────────────────────────────────────────────
        builder.HasOne(r => r.MedicalCode)
            .WithMany()
            .HasForeignKey(r => r.MedicalCodeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Primary access path: all verification records for a given medical code.
        builder.HasIndex(r => new { r.MedicalCodeId, r.CreatedAt })
            .HasDatabaseName("ix_hallucination_records_medical_code_id_created_at");

        // Accelerates daily aggregation queries filtering on Unsupported.
        builder.HasIndex(r => r.SourceSupportStatus)
            .HasDatabaseName("ix_hallucination_records_source_support_status");

        // Date-range filtering for the aggregation job.
        builder.HasIndex(r => r.VerifiedAt)
            .HasDatabaseName("ix_hallucination_records_verified_at");
    }
}
