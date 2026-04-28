using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="MigrationVerificationLog"/>
/// (US_091 task_002, AC-5, DR-032).
/// Table: <c>migration_verification_logs</c>.
/// </summary>
public sealed class MigrationVerificationLogConfiguration
    : IEntityTypeConfiguration<MigrationVerificationLog>
{
    public void Configure(EntityTypeBuilder<MigrationVerificationLog> builder)
    {
        builder.ToTable("migration_verification_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MigrationName)
               .IsRequired()
               .HasMaxLength(300);

        builder.Property(x => x.WarningDetails)
               .HasMaxLength(4000);

        builder.Property(x => x.ErrorDetails)
               .HasMaxLength(4000);

        builder.HasIndex(x => x.VerifiedAtUtc)
               .HasDatabaseName("ix_migration_verification_logs_verified_at_utc");
    }
}
