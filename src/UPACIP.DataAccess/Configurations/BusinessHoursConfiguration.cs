using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="BusinessHours"/> (US_059 AC-3).
///
/// Table: <c>business_hours</c>
///
/// Constraints:
///   - Unique on <c>DayOfWeek</c>: one row per day (7 rows total, seeded on first migration).
///   - Check: <c>IsClosed = true OR (OpenTime IS NOT NULL AND CloseTime IS NOT NULL AND OpenTime &lt; CloseTime)</c>:
///     ensures valid open/close times are provided for open days.
///
/// Indexes:
///   - <c>ix_business_hours_day_of_week</c>: unique covering index for O(1) day lookups.
///
/// Seed data: Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed.
///
/// FK: UpdatedByUserId → asp_net_users.Id ON DELETE SET NULL (preserves hours config if admin deleted).
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class BusinessHoursConfiguration : IEntityTypeConfiguration<BusinessHours>
{
    // ── Stable seed GUIDs — do NOT change after first migration ──────────────
    internal static readonly Guid SundayId    = new("b0590001-0059-0001-0000-000000000000");
    internal static readonly Guid MondayId    = new("b0590002-0059-0001-0000-000000000000");
    internal static readonly Guid TuesdayId   = new("b0590003-0059-0001-0000-000000000000");
    internal static readonly Guid WednesdayId = new("b0590004-0059-0001-0000-000000000000");
    internal static readonly Guid ThursdayId  = new("b0590005-0059-0001-0000-000000000000");
    internal static readonly Guid FridayId    = new("b0590006-0059-0001-0000-000000000000");
    internal static readonly Guid SaturdayId  = new("b0590007-0059-0001-0000-000000000000");

    public void Configure(EntityTypeBuilder<BusinessHours> builder)
    {
        builder.ToTable(
            "business_hours",
            t =>
            {
                // Check constraint: if not closed, both open and close times must be provided
                // and open_time must precede close_time.
                t.HasCheckConstraint(
                    "ck_business_hours_open_close_valid",
                    "\"IsClosed\" = true OR (\"OpenTime\" IS NOT NULL AND \"CloseTime\" IS NOT NULL AND \"OpenTime\" < \"CloseTime\")");
            });

        builder.HasKey(h => h.BusinessHoursId);
        builder.Property(h => h.BusinessHoursId).ValueGeneratedOnAdd();

        builder.Property(h => h.DayOfWeek)
            .IsRequired();

        // TimeOnly maps to PostgreSQL time without time zone.
        builder.Property(h => h.OpenTime)
            .IsRequired(false);

        builder.Property(h => h.CloseTime)
            .IsRequired(false);

        builder.Property(h => h.IsClosed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(h => h.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(h => h.UpdatedByUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        // ── FK to updating admin — SET NULL preserves the hours config if the user is deleted ──
        builder.HasOne(h => h.UpdatedByUser)
            .WithMany()
            .HasForeignKey(h => h.UpdatedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Unique index on DayOfWeek — enforces one row per day ─────────────
        builder.HasIndex(h => h.DayOfWeek)
            .IsUnique()
            .HasDatabaseName("ix_business_hours_day_of_week");

        // ── Seed default business hours ───────────────────────────────────────
        var seedDate = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            // Sunday — closed
            new BusinessHours
            {
                BusinessHoursId = SundayId,
                DayOfWeek       = 0,
                IsClosed        = true,
                OpenTime        = null,
                CloseTime       = null,
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            },
            // Monday — 08:00–17:00
            new BusinessHours
            {
                BusinessHoursId = MondayId,
                DayOfWeek       = 1,
                IsClosed        = false,
                OpenTime        = new TimeOnly(8, 0),
                CloseTime       = new TimeOnly(17, 0),
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            },
            // Tuesday — 08:00–17:00
            new BusinessHours
            {
                BusinessHoursId = TuesdayId,
                DayOfWeek       = 2,
                IsClosed        = false,
                OpenTime        = new TimeOnly(8, 0),
                CloseTime       = new TimeOnly(17, 0),
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            },
            // Wednesday — 08:00–17:00
            new BusinessHours
            {
                BusinessHoursId = WednesdayId,
                DayOfWeek       = 3,
                IsClosed        = false,
                OpenTime        = new TimeOnly(8, 0),
                CloseTime       = new TimeOnly(17, 0),
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            },
            // Thursday — 08:00–17:00
            new BusinessHours
            {
                BusinessHoursId = ThursdayId,
                DayOfWeek       = 4,
                IsClosed        = false,
                OpenTime        = new TimeOnly(8, 0),
                CloseTime       = new TimeOnly(17, 0),
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            },
            // Friday — 08:00–17:00
            new BusinessHours
            {
                BusinessHoursId = FridayId,
                DayOfWeek       = 5,
                IsClosed        = false,
                OpenTime        = new TimeOnly(8, 0),
                CloseTime       = new TimeOnly(17, 0),
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            },
            // Saturday — 09:00–13:00
            new BusinessHours
            {
                BusinessHoursId = SaturdayId,
                DayOfWeek       = 6,
                IsClosed        = false,
                OpenTime        = new TimeOnly(9, 0),
                CloseTime       = new TimeOnly(13, 0),
                UpdatedAt       = seedDate,
                UpdatedByUserId = null,
            });
    }
}
