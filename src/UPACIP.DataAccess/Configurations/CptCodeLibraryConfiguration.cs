using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent API configuration for the <see cref="CptCodeLibrary"/> reference table
/// (US_048, AC-4, task_003_db_cpt_code_library).
///
/// Naming convention: snake_case table and column names consistent with PostgreSQL
/// conventions used throughout this project.
///
/// Indexes:
/// <list type="bullet">
///   <item>Unique on <c>cpt_code</c> — fast point-lookup and duplicate prevention.</item>
///   <item>Composite on (<c>category</c>, <c>is_active</c>) — category-filtered active-code queries.</item>
///   <item>Single on <c>is_active</c> — active-code filter used by revalidation scans.</item>
/// </list>
/// </summary>
public sealed class CptCodeLibraryConfiguration : IEntityTypeConfiguration<CptCodeLibrary>
{
    public void Configure(EntityTypeBuilder<CptCodeLibrary> builder)
    {
        builder.ToTable("cpt_code_library");

        // Dedicated PK — not the BaseEntity Id convention (reference table pattern).
        builder.HasKey(e => e.CptCodeId);
        builder.Property(e => e.CptCodeId).ValueGeneratedOnAdd();

        builder.Property(e => e.CptCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EffectiveDate)
            .IsRequired();

        builder.Property(e => e.ExpirationDate)
            .IsRequired(false);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        // Unique index on CPT code value — fast lookup by code and prevents duplicate inserts
        // during quarterly refresh (idempotent re-run safety, DR-029).
        builder.HasIndex(e => e.CptCode)
            .IsUnique()
            .HasDatabaseName("uq_cpt_code_library_cpt_code");

        // Composite index for category-filtered active-code queries used by the
        // revalidation service and category-scoped RAG context building (AC-4).
        builder.HasIndex(e => new { e.Category, e.IsActive })
            .HasDatabaseName("ix_cpt_code_library_category_is_active");

        // Single index on is_active for full active-code scans during revalidation (AC-4).
        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("ix_cpt_code_library_is_active");
    }
}
