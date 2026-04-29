using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="ComplianceRule"/> (US_093, edge case 2).
/// Table: <c>compliance_rules</c>.
/// Unique index on rule_name enforces upsert semantics used by the compliance rule management API.
/// </summary>
public sealed class ComplianceRuleConfiguration : IEntityTypeConfiguration<ComplianceRule>
{
    public void Configure(EntityTypeBuilder<ComplianceRule> builder)
    {
        builder.ToTable("compliance_rules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RuleName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Category)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasMaxLength(500)
            .IsRequired();

        // EvaluationCriteriaJson holds a JSON object; TEXT avoids length limits.
        builder.Property(r => r.EvaluationCriteriaJson)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.Severity)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.HipaaReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.RemediationGuidance)
            .HasMaxLength(1000);

        // Unique rule name — enables safe upsert by compliance officers without code changes.
        builder.HasIndex(r => r.RuleName)
            .IsUnique()
            .HasDatabaseName("uix_compliance_rules_rule_name");

        // Index for filtering by category and active state during evaluation runs.
        builder.HasIndex(r => new { r.Category, r.IsActive })
            .HasDatabaseName("ix_compliance_rules_category_active");
    }
}
