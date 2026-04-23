using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="BundlingEdit"/> table
/// (US_051, AC-4, task_003_db_payer_rules_schema).
///
/// Composite unique index on (<c>column1_code</c>, <c>column2_code</c>) prevents
/// duplicate edits and supports idempotent seed re-runs.
/// </summary>
public sealed class BundlingEditConfiguration : IEntityTypeConfiguration<BundlingEdit>
{
    public void Configure(EntityTypeBuilder<BundlingEdit> builder)
    {
        builder.ToTable("bundling_edits");

        builder.HasKey(e => e.EditId);
        builder.Property(e => e.EditId).ValueGeneratedOnAdd();

        builder.Property(e => e.Column1Code).IsRequired().HasMaxLength(10);
        builder.Property(e => e.Column2Code).IsRequired().HasMaxLength(10);

        builder.Property(e => e.EditType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ModifierAllowed).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.AllowedModifiers).IsRequired().HasMaxLength(200).HasDefaultValue("[]");
        builder.Property(e => e.Source).IsRequired().HasMaxLength(20).HasDefaultValue("NCCI");
        builder.Property(e => e.EffectiveDate).IsRequired();
        builder.Property(e => e.ExpirationDate).IsRequired(false);
        builder.Property(e => e.CreatedAt).IsRequired();

        // Composite unique index: one edit per code pair (supports idempotent seed)
        builder.HasIndex(e => new { e.Column1Code, e.Column2Code })
            .IsUnique()
            .HasDatabaseName("uq_bundling_edits_column1_column2");
    }
}
