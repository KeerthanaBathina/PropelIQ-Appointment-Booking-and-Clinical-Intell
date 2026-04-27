using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();

        builder.Property(q => q.Priority)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        // Status stored as enum name string (e.g. "Waiting", "NoShow", "ArrivedLate").
        // Longest value: "ArrivedLate" = 11 chars — fits within 15-char column.
        builder.Property(q => q.Status)
            .HasConversion<string>()
            .HasMaxLength(15)
            .IsRequired();

        // ── US_052 — Cancellation (AC-3) ──────────────────────────────────────
        builder.Property(q => q.CancelledAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // ── US_052 — No-show override fields (edge case) ─────────────────────
        builder.Property(q => q.OverrideReason)
            .HasColumnType("text")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(q => q.OverriddenByUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        // ── Optimistic concurrency (TR-015) ───────────────────────────────────
        // EF Core adds WHERE "Version" = @p in UPDATE statements.
        // The column is incremented by the service layer before calling SaveChangesAsync.
        builder.Property(q => q.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        // ── US_054 — Priority queue position ─────────────────────────────────
        // Stored 1-based position; recalculated on every priority/reorder mutation.
        // Default 0 means "not yet positioned" (pre-first-calculation rows).
        builder.Property(q => q.QueuePosition)
            .HasDefaultValue(0)
            .IsRequired();

        // ── US_055 — Auto no-show detection flags ────────────────────────────
        builder.Property(q => q.IsAutoNoShow)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(q => q.IsDelayedDetection)
            .HasDefaultValue(false)
            .IsRequired();

        // Full unique index: one QueueEntry per Appointment (one-to-one relationship).
        // EF Core uses this index when resolving the HasOne.WithOne navigation.
        builder.HasIndex(q => q.AppointmentId)
            .IsUnique()
            .HasDatabaseName("ix_queue_entries_appointment_id");

        // Composite index: daily queue retrieval sorted by status + created time (NFR-004).
        builder.HasIndex(q => new { q.Status, q.CreatedAt })
            .HasDatabaseName("IX_queue_entries_status_created_at");

        // Composite index (US_056 AC-2): supports filtered queue queries combining status,
        // priority tier, and creation time for the queue dashboard AND logic filter:
        //   WHERE status = @status AND priority = @priority ORDER BY created_at
        // Leading status column enables selective scans for common status values (e.g. 'Waiting').
        builder.HasIndex(q => new { q.Status, q.Priority, q.CreatedAt })
            .HasDatabaseName("ix_queue_entries_status_priority_created_at");

        // US_054 — Composite index: priority tier + position ordering for sorted queue reads.
        // Priority is DESC (urgent first) and QueuePosition is ASC (lowest number first).
        builder.HasIndex(q => new { q.Priority, q.QueuePosition })
            .HasDatabaseName("IX_QueueEntry_Priority_Position")
            .IsDescending(true, false);

        // Partial filtered index (US_057 AC-4): optimises the dashboard active-queue count query:
        //   WHERE (status = 'Waiting' OR status = 'InVisit') AND arrival_timestamp >= @todayStart
        // Covering only the two active statuses (~30 % of rows in steady state) keeps the index
        // compact and lets PostgreSQL skip all Completed / Cancelled / NoShow entries entirely.
        builder.HasIndex(q => new { q.Status, q.ArrivalTimestamp })
            .HasFilter("status IN ('Waiting', 'InVisit')")
            .HasDatabaseName("ix_queue_entries_status_arrival_timestamp");

        // Partial unique index and CHECK constraints are created via raw SQL in the migration
        // (AddArrivalStatusSupport) because EF Core Fluent API does not support partial index
        // filter expressions or named CHECK constraints portably across providers.

        // The inverse side (Appointment → QueueEntry) is configured in AppointmentConfiguration.
        builder.HasOne(q => q.Appointment)
            .WithOne(a => a.QueueEntry)
            .HasForeignKey<QueueEntry>(q => q.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

