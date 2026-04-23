using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class CodingAuditLogConfiguration : IEntityTypeConfiguration<CodingAuditLog>
{
    public void Configure(EntityTypeBuilder<CodingAuditLog> builder)
    {
        builder.ToTable("coding_audit_log");

        // CodingAuditLog uses LogId as its primary key (immutable pattern — no BaseEntity).
        builder.HasKey(c => c.LogId);
        builder.Property(c => c.LogId).ValueGeneratedOnAdd();

        // Action stored as string; max 25 to accommodate all CodingAuditAction member names
        // (e.g. "DeprecatedBlocked" = 16 chars; headroom for future actions).
        builder.Property(c => c.Action)
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired();

        builder.Property(c => c.OldCodeValue).IsRequired().HasMaxLength(20);
        builder.Property(c => c.NewCodeValue).IsRequired().HasMaxLength(20);

        builder.Property(c => c.Justification)
            .HasMaxLength(1000)
            .IsRequired(false);

        // UserId is nullable: ON DELETE SET NULL preserves audit records when a user is
        // soft-deleted or hard-deleted (DR-016). Nullable also supports future system events.
        builder.Property(c => c.UserId).IsRequired(false);

        // Timestamp is immutable — stored as timestamptz (UTC offset preserved).
        builder.Property(c => c.Timestamp).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // ─────────────────────────────────────────────────────────────────────
        // Relationships
        // ─────────────────────────────────────────────────────────────────────

        // Restrict delete so the audit trail is preserved even if the medical code is
        // logically deleted via a status flag in the future.
        builder.HasOne(c => c.MedicalCode)
            .WithMany(m => m.CodingAuditLogs)
            .HasForeignKey(c => c.MedicalCodeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict delete to prevent cascading removal of audit trail rows.
        builder.HasOne(c => c.Patient)
            .WithMany()
            .HasForeignKey(c => c.PatientId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable; ON DELETE SET NULL preserves the audit record after user deletion.
        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ─────────────────────────────────────────────────────────────────────
        // Indexes (US_049 AC-2, AC-4)
        // ─────────────────────────────────────────────────────────────────────

        // Per-code audit trail lookup — primary access pattern for the review UI.
        builder.HasIndex(c => c.MedicalCodeId)
            .HasDatabaseName("ix_coding_audit_log_medical_code_id");

        // Patient-level audit history ordered by recency.
        builder.HasIndex(c => new { c.PatientId, c.Timestamp })
            .HasDatabaseName("ix_coding_audit_log_patient_id_timestamp");
    }
}
