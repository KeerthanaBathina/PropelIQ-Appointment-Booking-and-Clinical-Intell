using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ProviderAvailabilityTemplate"/> (US_017).
///
/// Table: <c>provider_availability_templates</c>
///
/// Constraints:
///   - Unique on <c>(provider_id, day_of_week, start_time)</c>: prevents overlapping templates.
///   - Check <c>slot_duration_minutes &gt; 0</c>: enforces positive slot duration.
///   - Check <c>end_time &gt; start_time</c>: enforces valid time range.
///   - FK to <c>asp_net_users.id</c> ON DELETE CASCADE: removing a user removes their templates.
///
/// Indexes:
///   - <c>ix_provider_availability_templates_provider_id</c>: fast lookup by provider.
///   - <c>ix_provider_availability_templates_provider_id_day_of_week</c>: composite for
///     day-of-week filtered queries (slot generation scans by provider + day).
///
/// Seed data (2 providers, Mon–Fri, different specialties):
///   - Provider A (Dr. Emily Chen): Mon–Fri, 08:00–17:00, 30-min, General Checkup
///   - Provider B (Dr. Michael Park): Mon–Fri, 09:00–16:00, 30-min, Consultation
///   - Provider C (Dr. Lisa Wang): Mon/Wed/Fri, 10:00–15:00, 30-min, Follow-up
///
/// Provider GUIDs are stable so seed data is idempotent across re-runs.
/// They are not linked to real ApplicationUser rows in seed data (FK not enforced at seed time
/// in design-mode; runtime enforcement is through the FK constraint).
/// </summary>
public sealed class ProviderAvailabilityTemplateConfiguration
    : IEntityTypeConfiguration<ProviderAvailabilityTemplate>
{
    // ── Stable seed GUIDs — do NOT change after first migration ──────────────
    private static readonly Guid ProviderAId = new("d1e2f3a4-b5c6-7890-abcd-ef1234567890");
    private static readonly Guid ProviderBId = new("e2f3a4b5-c6d7-8901-bcde-f12345678901");
    private static readonly Guid ProviderCId = new("f3a4b5c6-d7e8-9012-cdef-123456789012");

    public void Configure(EntityTypeBuilder<ProviderAvailabilityTemplate> builder)
    {
        builder.ToTable(
            "provider_availability_templates",
            t =>
            {
                // Check constraint: slot duration must be positive
                t.HasCheckConstraint(
                    "ck_provider_availability_templates_slot_duration_positive",
                    "slot_duration_minutes > 0");

                // Check constraint: end_time must be after start_time
                t.HasCheckConstraint(
                    "ck_provider_availability_templates_end_after_start",
                    "end_time > start_time");
            });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.ProviderName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.DayOfWeek)
            .IsRequired();

        // TimeOnly is stored as time without time zone in PostgreSQL (Npgsql default mapping).
        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.EndTime).IsRequired();

        builder.Property(t => t.SlotDurationMinutes)
            .IsRequired()
            .HasDefaultValue(30);

        builder.Property(t => t.AppointmentType)
            .HasMaxLength(50)
            .IsRequired()
            .HasDefaultValue("General Checkup");

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // ── Indexes ───────────────────────────────────────────────────────────

        builder.HasIndex(t => t.ProviderId)
            .HasDatabaseName("ix_provider_availability_templates_provider_id");

        // Composite index: slot generation queries filter by provider + day of week
        builder.HasIndex(t => new { t.ProviderId, t.DayOfWeek })
            .HasDatabaseName("ix_provider_availability_templates_provider_id_day_of_week");

        // Unique constraint: one template per (provider, day, start-time)
        builder.HasIndex(t => new { t.ProviderId, t.DayOfWeek, t.StartTime })
            .IsUnique()
            .HasDatabaseName("uq_provider_availability_templates_provider_day_start");

        // ── FK to ApplicationUser (staff provider) ────────────────────────────
        builder.HasOne(t => t.Provider)
            .WithMany()
            .HasForeignKey(t => t.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Seed data ─────────────────────────────────────────────────────────
        // Three providers × 5 working days (Mon–Fri = DayOfWeek 1–5) for Provider A and B,
        // and Mon/Wed/Fri (1, 3, 5) for Provider C.
        // Total: 13 seed rows. GUIDs are stable and deterministic.

        var seeds = BuildSeedData();
        builder.HasData(seeds);
    }

    private static IEnumerable<ProviderAvailabilityTemplate> BuildSeedData()
    {
        var now = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc);

        // Provider A — Dr. Emily Chen: Mon–Fri, 08:00–17:00, General Checkup
        int[] weekdays = [1, 2, 3, 4, 5];
        foreach (var dow in weekdays)
        {
            yield return new ProviderAvailabilityTemplate
            {
                Id                 = GuidForSeed(ProviderAId, dow, 1),
                ProviderId         = ProviderAId,
                ProviderName       = "Dr. Emily Chen",
                DayOfWeek          = dow,
                StartTime          = new TimeOnly(8, 0),
                EndTime            = new TimeOnly(17, 0),
                SlotDurationMinutes = 30,
                AppointmentType    = "General Checkup",
                IsActive           = true,
                CreatedAt          = now,
                UpdatedAt          = now,
            };
        }

        // Provider B — Dr. Michael Park: Mon–Fri, 09:00–16:00, Consultation
        foreach (var dow in weekdays)
        {
            yield return new ProviderAvailabilityTemplate
            {
                Id                 = GuidForSeed(ProviderBId, dow, 2),
                ProviderId         = ProviderBId,
                ProviderName       = "Dr. Michael Park",
                DayOfWeek          = dow,
                StartTime          = new TimeOnly(9, 0),
                EndTime            = new TimeOnly(16, 0),
                SlotDurationMinutes = 30,
                AppointmentType    = "Consultation",
                IsActive           = true,
                CreatedAt          = now,
                UpdatedAt          = now,
            };
        }

        // Provider C — Dr. Lisa Wang: Mon/Wed/Fri, 10:00–15:00, Follow-up
        int[] mwf = [1, 3, 5];
        foreach (var dow in mwf)
        {
            yield return new ProviderAvailabilityTemplate
            {
                Id                 = GuidForSeed(ProviderCId, dow, 3),
                ProviderId         = ProviderCId,
                ProviderName       = "Dr. Lisa Wang",
                DayOfWeek          = dow,
                StartTime          = new TimeOnly(10, 0),
                EndTime            = new TimeOnly(15, 0),
                SlotDurationMinutes = 30,
                AppointmentType    = "Follow-up",
                IsActive           = true,
                CreatedAt          = now,
                UpdatedAt          = now,
            };
        }
    }

    /// <summary>
    /// Produces a stable, deterministic GUID per (providerId, dayOfWeek, series) triplet.
    /// Using XOR + rotation avoids collision while keeping GUIDs human-readable in logs.
    /// </summary>
    private static Guid GuidForSeed(Guid providerId, int dayOfWeek, int series)
    {
        var bytes = providerId.ToByteArray();
        bytes[0] ^= (byte)(dayOfWeek * 17);
        bytes[1] ^= (byte)(series * 31);
        bytes[15] ^= (byte)(dayOfWeek + series);
        return new Guid(bytes);
    }
}
