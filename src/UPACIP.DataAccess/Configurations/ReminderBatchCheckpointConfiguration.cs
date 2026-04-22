using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core table mapping and index configuration for <see cref="ReminderBatchCheckpoint"/>
/// (US_035 EC-1, EC-2).
/// </summary>
public sealed class ReminderBatchCheckpointConfiguration
    : IEntityTypeConfiguration<ReminderBatchCheckpoint>
{
    public void Configure(EntityTypeBuilder<ReminderBatchCheckpoint> builder)
    {
        builder.ToTable("reminder_batch_checkpoints");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        // BatchType stored as integer (compact; range-scan friendly)
        builder.Property(r => r.BatchType)
            .IsRequired();

        builder.Property(r => r.WindowDateUtc)
            .IsRequired();

        builder.Property(r => r.WindowStartUtc)
            .IsRequired();

        builder.Property(r => r.WindowEndUtc)
            .IsRequired();

        builder.Property(r => r.LastProcessedAppointmentId)
            .IsRequired(false);

        builder.Property(r => r.LastProcessedAppointmentTimeUtc)
            .IsRequired(false);

        builder.Property(r => r.ProcessedCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.SkippedCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.FailedCount)
            .HasDefaultValue(0)
            .IsRequired();

        // RunStatus stored as string for human-readable operational queries
        builder.Property(r => r.RunStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.RunId)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // ── Unique constraint (EC-2): one checkpoint per (BatchType, WindowDateUtc) ──
        // Enforces that 24-hour and 2-hour batches cannot share a checkpoint row.
        // The worker UPSERTs on this key so only one row exists per window per day.
        builder.HasIndex(r => new { r.BatchType, r.WindowDateUtc })
            .IsUnique()
            .HasDatabaseName("uq_reminder_batch_checkpoints_type_window");

        // Fast resume lookup: find the latest running checkpoint for a given batch type
        builder.HasIndex(r => new { r.BatchType, r.RunStatus, r.UpdatedAt })
            .HasDatabaseName("ix_reminder_batch_checkpoints_type_status_updated");

        // Pruning query: delete rows older than 7 days with a simple date filter
        builder.HasIndex(r => r.CreatedAt)
            .HasDatabaseName("ix_reminder_batch_checkpoints_created_at");
    }
}
