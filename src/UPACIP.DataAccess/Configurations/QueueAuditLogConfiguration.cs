using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="QueueAuditLog"/> entity (US_054 AC-3, TR-028).
///
/// Maps to the <c>queue_audit_logs</c> table. Append-only — no update or delete paths
/// are exposed via the application. The audit record is preserved even when the related
/// <see cref="QueueEntry"/> or <see cref="ApplicationUser"/> is deleted:
///   - QueueId FK:      ON DELETE RESTRICT (audit trail must survive queue-entry deletion)
///   - StaffUserId FK:  ON DELETE SET NULL  (preserves record when user is hard-deleted — DR-016)
/// </summary>
public sealed class QueueAuditLogConfiguration : IEntityTypeConfiguration<QueueAuditLog>
{
    public void Configure(EntityTypeBuilder<QueueAuditLog> builder)
    {
        builder.ToTable("queue_audit_logs");

        // Surrogate UUID primary key — no BaseEntity inheritance.
        builder.HasKey(a => a.AuditId);
        builder.Property(a => a.AuditId).ValueGeneratedOnAdd();

        // ── Column constraints ────────────────────────────────────────────────

        // ActionType: fixed vocabulary "PRIORITY_CHANGE" | "REORDER" (max 15 chars; 20 gives headroom).
        builder.Property(a => a.ActionType)
            .HasColumnType("character varying(20)")
            .HasMaxLength(20)
            .IsRequired();

        // Priority string values: "normal" | "urgent" — both ≤ 6 chars; 10-char column gives headroom.
        builder.Property(a => a.OriginalPriority)
            .HasColumnType("character varying(10)")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(a => a.NewPriority)
            .HasColumnType("character varying(10)")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(a => a.OriginalPosition).IsRequired();
        builder.Property(a => a.NewPosition).IsRequired();

        // Immutable timestamp — stored as timestamptz (UTC offset preserved).
        builder.Property(a => a.Timestamp)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // StaffUserId: nullable UUID — supports system events and post-deletion records.
        builder.Property(a => a.StaffUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        // QueueId: non-nullable FK to queue_entries.
        builder.Property(a => a.QueueId)
            .HasColumnType("uuid")
            .IsRequired();

        // ── Relationships ─────────────────────────────────────────────────────

        // Restrict delete: audit records must survive queue-entry deletion.
        // QueueEntry.QueueAuditLogs navigation is intentionally omitted from QueueEntry
        // to keep the entity focused; this side owns the relationship.
        builder.HasOne(a => a.QueueEntry)
            .WithMany()
            .HasForeignKey(a => a.QueueId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable FK to AspNetUsers — ON DELETE SET NULL preserves audit trail
        // when a staff user account is hard-deleted (DR-016).
        builder.HasOne(a => a.StaffUser)
            .WithMany()
            .HasForeignKey(a => a.StaffUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Indexes ───────────────────────────────────────────────────────────

        // US_054: efficient query of audit history for a given queue entry, newest-first.
        builder.HasIndex(a => new { a.QueueId, a.Timestamp })
            .HasDatabaseName("IX_QueueAuditLog_QueueId_CreatedAt")
            .IsDescending(false, true);
    }
}
