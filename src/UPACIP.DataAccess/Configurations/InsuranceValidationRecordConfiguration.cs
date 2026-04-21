using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="InsuranceValidationRecord"/> reference entity.
///
/// Maps to the <c>insurance_validation_records</c> table, which holds the dummy insurance
/// dataset used by <c>InsurancePrecheckService</c> to perform deterministic soft validation
/// without external payer system integrations (US_031, AC-2, FR-033).
///
/// Records are seeded by the AddMinorGuardianAndInsuranceValidation migration and
/// can be deactivated (not deleted) via the <see cref="InsuranceValidationRecord.IsActive"/> flag.
/// </summary>
public sealed class InsuranceValidationRecordConfiguration : IEntityTypeConfiguration<InsuranceValidationRecord>
{
    public void Configure(EntityTypeBuilder<InsuranceValidationRecord> builder)
    {
        builder.ToTable("insurance_validation_records");

        // Integer identity PK — small cardinality reference data, not FK-referenced by other tables.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.Property(r => r.ProviderName)
            .HasMaxLength(200)
            .IsRequired();

        // provider_keyword: lowercase search term compared case-insensitively against
        // patient-supplied provider strings.  Indexed for fast in-DB lookups if the
        // service is updated to query the table directly.
        builder.Property(r => r.ProviderKeyword)
            .HasColumnName("provider_keyword")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(r => r.ProviderKeyword)
            .HasDatabaseName("ix_insurance_validation_records_provider_keyword");

        builder.Property(r => r.PolicyPrefix)
            .HasColumnName("policy_prefix")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
