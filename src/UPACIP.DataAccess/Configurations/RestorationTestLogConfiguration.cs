using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="RestorationTestLog"/> (US_089 task_003, AC-3).
/// Table: <c>restoration_test_logs</c>.
/// </summary>
public sealed class RestorationTestLogConfiguration : IEntityTypeConfiguration<RestorationTestLog>
{
    public void Configure(EntityTypeBuilder<RestorationTestLog> builder)
    {
        builder.ToTable("restoration_test_logs");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.BackupFileName)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(r => r.PerformedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.ErrorDetails)
            .HasMaxLength(4000);

        // Index for chronological queries and quarterly compliance reporting.
        builder.HasIndex(r => r.CreatedAtUtc)
            .HasDatabaseName("ix_restoration_test_logs_created_at_utc");
    }
}
