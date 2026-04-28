using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="RecoveryLog"/> (US_090 task_002, AC-2).
/// Table: <c>recovery_logs</c>.
/// </summary>
public sealed class RecoveryLogConfiguration : IEntityTypeConfiguration<RecoveryLog>
{
    public void Configure(EntityTypeBuilder<RecoveryLog> builder)
    {
        builder.ToTable("recovery_logs");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.BaseBackupUsed)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(r => r.PerformedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.FallbackReason)
            .HasMaxLength(2000);

        builder.Property(r => r.ErrorMessage)
            .HasMaxLength(2000);

        // Index for chronological queries and PITR audit reporting.
        builder.HasIndex(r => r.CreatedAtUtc)
            .HasDatabaseName("ix_recovery_logs_created_at_utc");
    }
}
