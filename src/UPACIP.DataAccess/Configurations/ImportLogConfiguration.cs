using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="ImportLog"/>
/// (US_092 task_002, AC-2, AC-3).
/// Table: <c>import_logs</c>.
/// </summary>
public sealed class ImportLogConfiguration : IEntityTypeConfiguration<ImportLog>
{
    public void Configure(EntityTypeBuilder<ImportLog> builder)
    {
        builder.ToTable("import_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType)
               .IsRequired()
               .HasMaxLength(50);

        builder.Property(x => x.FileName)
               .IsRequired()
               .HasMaxLength(260);

        builder.Property(x => x.Status)
               .IsRequired()
               .HasMaxLength(30);

        builder.Property(x => x.PerformedBy)
               .IsRequired()
               .HasMaxLength(450);

        builder.Property(x => x.ErrorReportJson)
               .HasMaxLength(65536); // ~64 KB inline cap

        builder.Property(x => x.FullErrorReportPath)
               .HasMaxLength(500);

        builder.HasIndex(x => x.CreatedAtUtc)
               .HasDatabaseName("ix_import_logs_created_at_utc");

        builder.HasIndex(x => new { x.EntityType, x.Status })
               .HasDatabaseName("ix_import_logs_entity_type_status");
    }
}
