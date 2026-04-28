using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="BackupLog"/> (US_088, AC-3).
/// Table: <c>backup_logs</c>.
/// </summary>
public sealed class BackupLogConfiguration : IEntityTypeConfiguration<BackupLog>
{
    public void Configure(EntityTypeBuilder<BackupLog> builder)
    {
        builder.ToTable("backup_logs");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.FileName)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(b => b.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.Checksum)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(b => b.ErrorMessage)
            .HasMaxLength(2000);

        // Index for chronological queries and monitoring dashboards.
        builder.HasIndex(b => b.CreatedAtUtc)
            .HasDatabaseName("ix_backup_logs_created_at_utc");
    }
}
