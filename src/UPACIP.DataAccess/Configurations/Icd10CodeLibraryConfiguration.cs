using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class Icd10CodeLibraryConfiguration : IEntityTypeConfiguration<Icd10CodeLibrary>
{
    public void Configure(EntityTypeBuilder<Icd10CodeLibrary> builder)
    {
        builder.ToTable("icd10_code_library");

        // Dedicated primary key — not the BaseEntity Id convention (reference table pattern).
        builder.HasKey(e => e.LibraryEntryId);
        builder.Property(e => e.LibraryEntryId).ValueGeneratedOnAdd();

        builder.Property(e => e.CodeValue)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.EffectiveDate)
            .IsRequired();

        builder.Property(e => e.DeprecatedDate)
            .IsRequired(false);

        builder.Property(e => e.ReplacementCode)
            .HasMaxLength(10)
            .IsRequired(false);

        builder.Property(e => e.LibraryVersion)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.IsCurrent)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        // Composite index for active-code lookups used by the coding validation service (DR-015).
        // Allows the query: WHERE code_value = @code AND is_current = TRUE to hit index-only scan.
        builder.HasIndex(e => new { e.CodeValue, e.IsCurrent })
            .HasDatabaseName("ix_icd10_code_library_code_value_is_current");

        // Index on category for category-filtered RAG retrieval context building (AC-4).
        builder.HasIndex(e => e.Category)
            .HasDatabaseName("ix_icd10_code_library_category");

        // Unique constraint: one entry per (code, library version) — prevents duplicate inserts
        // during quarterly refresh and keeps the versioned audit trail clean (task plan step 6).
        builder.HasIndex(e => new { e.CodeValue, e.LibraryVersion })
            .IsUnique()
            .HasDatabaseName("uq_icd10_code_library_code_version");
    }
}
