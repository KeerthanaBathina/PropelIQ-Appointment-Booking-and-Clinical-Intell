using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="CodeModifier"/> table
/// (US_051, AC-4, task_003_db_payer_rules_schema).
/// </summary>
public sealed class CodeModifierConfiguration : IEntityTypeConfiguration<CodeModifier>
{
    public void Configure(EntityTypeBuilder<CodeModifier> builder)
    {
        builder.ToTable("code_modifiers");

        builder.HasKey(m => m.ModifierId);
        builder.Property(m => m.ModifierId).ValueGeneratedOnAdd();

        builder.Property(m => m.ModifierCode).IsRequired().HasMaxLength(5);
        builder.Property(m => m.ModifierDescription).IsRequired().HasMaxLength(500);
        builder.Property(m => m.ApplicableCodeTypes).IsRequired().HasMaxLength(50).HasDefaultValue("[\"cpt\"]");
        builder.Property(m => m.DocumentationRequired).IsRequired().HasDefaultValue(false);
        builder.Property(m => m.CreatedAt).IsRequired();

        // Unique constraint: one row per modifier code
        builder.HasIndex(m => m.ModifierCode)
            .IsUnique()
            .HasDatabaseName("uq_code_modifiers_modifier_code");
    }
}
