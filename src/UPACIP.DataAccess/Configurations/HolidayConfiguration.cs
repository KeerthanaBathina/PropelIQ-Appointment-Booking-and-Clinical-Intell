using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="Holiday"/> (US_059 AC-4).
///
/// Table: <c>holidays</c>
///
/// Constraints:
///   - Partial unique on <c>Date WHERE DeletedAt IS NULL</c>: prevents duplicate active
///     holidays for the same date while allowing re-creation after soft-deletion.
///
/// Indexes:
///   - <c>ix_holidays_date_active</c>: partial unique (active holidays only, see above).
///   - <c>ix_holidays_date</c>: full (non-partial) index for booking-validation queries that
///     need to check all holidays including deactivated records for historical accuracy.
///
/// FK: CreatedByUserId → asp_net_users.Id ON DELETE SET NULL (preserves holiday records if
///     the admin who created them is later removed).
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holidays");

        builder.HasKey(h => h.HolidayId);
        builder.Property(h => h.HolidayId).ValueGeneratedOnAdd();

        // ── Date column — PostgreSQL date type (no time component) ────────────
        builder.Property(h => h.Date)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(h => h.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(h => h.IsRecurring)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(h => h.IsHalfDay)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(h => h.CreatedByUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(h => h.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        // Soft-delete column: null = active, non-null = deactivated.
        builder.Property(h => h.DeletedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // ── FK to creating admin — SET NULL preserves holiday record on user deletion ──
        builder.HasOne(h => h.CreatedByUser)
            .WithMany()
            .HasForeignKey(h => h.CreatedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Partial unique index: active holidays only ────────────────────────
        // Prevents two active (non-deleted) holidays on the same date while allowing
        // a holiday on a given date to be re-created after soft-deletion.
        builder.HasIndex(h => h.Date)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_holidays_date_active");

        // ── Full index on Date for booking-validation queries ─────────────────
        // Used by the booking service to check all holidays (including deactivated ones)
        // when displaying historical appointment information.
        builder.HasIndex(h => h.Date)
            .HasDatabaseName("ix_holidays_date");
    }
}
