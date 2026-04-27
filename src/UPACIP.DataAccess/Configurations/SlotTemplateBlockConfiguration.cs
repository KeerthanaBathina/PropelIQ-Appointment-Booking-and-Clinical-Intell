using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="SlotTemplateBlock"/> (US_059 AC-1).
///
/// Table: <c>slot_template_blocks</c>
///
/// Constraints:
///   - Check <c>start_time &lt; end_time</c>: enforces valid time range at the DB level.
///   - FK to <c>slot_templates</c> ON DELETE CASCADE: removing a template removes all its blocks.
///
/// Indexes:
///   - <c>ix_slot_template_blocks_slot_template_id</c>: fast lookup of all blocks for a template.
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class SlotTemplateBlockConfiguration : IEntityTypeConfiguration<SlotTemplateBlock>
{
    public void Configure(EntityTypeBuilder<SlotTemplateBlock> builder)
    {
        builder.ToTable(
            "slot_template_blocks",
            t =>
            {
                // Check constraint: block must represent a valid time range.
                t.HasCheckConstraint(
                    "ck_slot_template_blocks_end_after_start",
                    "\"EndTime\" > \"StartTime\"");
            });

        builder.HasKey(b => b.BlockId);
        builder.Property(b => b.BlockId).ValueGeneratedOnAdd();

        builder.Property(b => b.SlotTemplateId)
            .HasColumnType("uuid")
            .IsRequired();

        // TimeOnly maps to PostgreSQL time without time zone.
        builder.Property(b => b.StartTime).IsRequired();
        builder.Property(b => b.EndTime).IsRequired();

        builder.Property(b => b.AppointmentType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(b => b.IsAvailable)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(b => b.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        // ── FK to parent template — CASCADE deletes all blocks when template is removed ──
        builder.HasOne(b => b.SlotTemplate)
            .WithMany(t => t.Blocks)
            .HasForeignKey(b => b.SlotTemplateId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // ── Index on SlotTemplateId for efficient block-load-by-template queries ──
        builder.HasIndex(b => b.SlotTemplateId)
            .HasDatabaseName("ix_slot_template_blocks_slot_template_id");
    }
}
