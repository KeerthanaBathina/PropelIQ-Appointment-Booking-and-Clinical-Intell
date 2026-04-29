using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent-API configuration for <see cref="CompliancePolicy"/> (US_093, AC-2).
/// Table: <c>compliance_policies</c>.
/// Composite index on (policy_type, version) supports type-filtered listing and version lookup.
/// </summary>
public sealed class CompliancePolicyConfiguration : IEntityTypeConfiguration<CompliancePolicy>
{
    public void Configure(EntityTypeBuilder<CompliancePolicy> builder)
    {
        builder.ToTable("compliance_policies");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PolicyType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Title)
            .HasMaxLength(200)
            .IsRequired();

        // Content stored as TEXT (unlimited) to accommodate full policy documents.
        builder.Property(p => p.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.HipaaReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.CreatedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(p => p.ApprovedBy)
            .HasMaxLength(256);

        // Composite index for listing policies by type with version ordering.
        builder.HasIndex(p => new { p.PolicyType, p.Version })
            .HasDatabaseName("ix_compliance_policies_type_version");

        // Index for fast active-policy queries during audits.
        builder.HasIndex(p => p.Status)
            .HasDatabaseName("ix_compliance_policies_status");
    }
}
