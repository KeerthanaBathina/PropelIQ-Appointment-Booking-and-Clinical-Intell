using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="SlotTemplate"/> (US_059 AC-1, AC-2).
///
/// Table: <c>slot_templates</c>
///
/// Constraints:
///   - Composite unique on <c>(ProviderId, DayOfWeek)</c>: one template per provider/day.
///   - <c>Version</c> as EF Core optimistic-concurrency token (prevents lost-update anomalies
///     when two admins save the same template concurrently).
///
/// Indexes:
///   - <c>ix_slot_templates_provider_id_day_of_week</c>: composite unique (see above).
///   - <c>ix_slot_templates_provider_id</c>: covers list-by-provider queries in the Admin UI.
///
/// FK: ProviderId → asp_net_users.Id ON DELETE RESTRICT (template must be explicitly removed
///     before a provider account can be deleted — prevents silent data loss).
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class SlotTemplateConfiguration : IEntityTypeConfiguration<SlotTemplate>
{
    public void Configure(EntityTypeBuilder<SlotTemplate> builder)
    {
        builder.ToTable("slot_templates");

        builder.HasKey(t => t.SlotTemplateId);
        builder.Property(t => t.SlotTemplateId).ValueGeneratedOnAdd();

        builder.Property(t => t.ProviderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.DayOfWeek)
            .IsRequired();

        // ── Optimistic-concurrency token ──────────────────────────────────────
        // EF Core includes Version in every UPDATE WHERE clause.
        // Stale writes throw DbUpdateConcurrencyException (DR-015).
        builder.Property(t => t.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        // ── FK to provider user — RESTRICT to protect against silent data loss ─
        builder.HasOne(t => t.Provider)
            .WithMany()
            .HasForeignKey(t => t.ProviderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // ── Composite unique index on (ProviderId, DayOfWeek) ─────────────────
        // Enforces exactly one template per provider/day combination.
        builder.HasIndex(t => new { t.ProviderId, t.DayOfWeek })
            .IsUnique()
            .HasDatabaseName("ix_slot_templates_provider_id_day_of_week");

        // ── Index on ProviderId alone for list-by-provider queries ────────────
        builder.HasIndex(t => t.ProviderId)
            .HasDatabaseName("ix_slot_templates_provider_id");
    }
}
