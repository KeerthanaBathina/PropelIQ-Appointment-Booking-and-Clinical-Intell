using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent API configuration for the <see cref="CptBundleRule"/> table
/// (US_048 AC-3, task_003_db_cpt_code_library).
///
/// Unique constraint on (<c>bundle_cpt_code</c>, <c>component_cpt_code</c>) prevents
/// duplicate rules and makes the quarterly seed script idempotent on re-run (DR-029).
///
/// Foreign keys reference <c>cpt_code_library.cpt_code</c> via the string value (not UUID)
/// so that bundle rules remain human-readable without a join.  Restrict-on-delete prevents
/// orphaned rules when a CPT code is removed from the library.
/// </summary>
public sealed class CptBundleRuleConfiguration : IEntityTypeConfiguration<CptBundleRule>
{
    public void Configure(EntityTypeBuilder<CptBundleRule> builder)
    {
        builder.ToTable("cpt_bundle_rules");

        builder.HasKey(e => e.BundleRuleId);
        builder.Property(e => e.BundleRuleId).ValueGeneratedOnAdd();

        builder.Property(e => e.BundleCptCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.ComponentCptCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.BundleDescription)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt).IsRequired();

        // Unique constraint: one rule per (bundle_code, component_code) pair — prevents duplicates
        // and enables idempotent ON CONFLICT DO NOTHING seed behaviour (DR-029).
        builder.HasIndex(e => new { e.BundleCptCode, e.ComponentCptCode })
            .IsUnique()
            .HasDatabaseName("uq_cpt_bundle_rules_bundle_component");

        // Index on bundle_cpt_code to quickly find all components belonging to a given bundle.
        builder.HasIndex(e => e.BundleCptCode)
            .HasDatabaseName("ix_cpt_bundle_rules_bundle_cpt_code");

        // Index on is_active for filtering active rules during the AI coding review pass.
        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("ix_cpt_bundle_rules_is_active");
    }
}
