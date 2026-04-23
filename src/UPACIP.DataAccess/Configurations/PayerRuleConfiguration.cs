using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="PayerRule"/> table
/// (US_051, AC-1, AC-2, task_003_db_payer_rules_schema).
///
/// Composite index on (<c>payer_id</c>, <c>primary_code</c>, <c>secondary_code</c>)
/// supports the hot-path lookup used by <c>PayerRuleValidationService</c>.
/// A separate partial index on <c>is_cms_default = true</c> speeds up the CMS-fallback
/// path when payer-specific rules are not available.
/// </summary>
public sealed class PayerRuleConfiguration : IEntityTypeConfiguration<PayerRule>
{
    public void Configure(EntityTypeBuilder<PayerRule> builder)
    {
        builder.ToTable("payer_rules");

        builder.HasKey(r => r.RuleId);
        builder.Property(r => r.RuleId).ValueGeneratedOnAdd();

        builder.Property(r => r.PayerId).HasMaxLength(50).IsRequired(false);
        builder.Property(r => r.PayerName).HasMaxLength(200).IsRequired(false);

        builder.Property(r => r.RuleType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.CodeType)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.PrimaryCode).IsRequired().HasMaxLength(20);
        builder.Property(r => r.SecondaryCode).HasMaxLength(20).IsRequired(false);
        builder.Property(r => r.RuleDescription).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.DenialReason).IsRequired().HasMaxLength(500);
        builder.Property(r => r.CorrectiveAction).IsRequired().HasMaxLength(500);

        builder.Property(r => r.Severity)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.IsCmsDefault).IsRequired().HasDefaultValue(false);
        builder.Property(r => r.EffectiveDate).IsRequired();
        builder.Property(r => r.ExpirationDate).IsRequired(false);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        // Composite lookup index: payer_id + primary_code + secondary_code (hot-path query)
        builder.HasIndex(r => new { r.PayerId, r.PrimaryCode, r.SecondaryCode })
            .HasDatabaseName("ix_payer_rules_payer_primary_secondary");

        // Index for CMS-default fallback path
        builder.HasIndex(r => r.IsCmsDefault)
            .HasDatabaseName("ix_payer_rules_is_cms_default");
    }
}
