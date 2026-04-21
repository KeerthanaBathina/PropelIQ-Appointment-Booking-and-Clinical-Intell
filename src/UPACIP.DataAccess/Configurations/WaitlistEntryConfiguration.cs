using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core table mapping and index configuration for <see cref="WaitlistEntry"/> (US_020).
/// </summary>
public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedOnAdd();

        builder.Property(w => w.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(w => w.AppointmentType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(w => w.PreferredDate)
            .IsRequired();

        builder.Property(w => w.PreferredStartTime)
            .IsRequired();

        builder.Property(w => w.PreferredEndTime)
            .IsRequired();

        builder.Property(w => w.PreferredProviderId)
            .IsRequired(false);

        builder.Property(w => w.ClaimToken)
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(w => w.OfferedSlotId)
            .HasMaxLength(120)
            .IsRequired(false);

        builder.Property(w => w.OfferedAtUtc).IsRequired(false);
        builder.Property(w => w.ClaimExpiresAtUtc).IsRequired(false);
        builder.Property(w => w.ClaimedAtUtc).IsRequired(false);
        builder.Property(w => w.LastNotifiedAtUtc).IsRequired(false);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Per-patient lookup (self-service view, EC-1 status check)
        builder.HasIndex(w => w.PatientId)
            .HasDatabaseName("ix_waitlist_entries_patient_id");

        // Processor fan-out — only Active entries are evaluated when a slot opens (AC-2)
        builder.HasIndex(w => w.Status)
            .HasDatabaseName("ix_waitlist_entries_status");

        // Fast criteria matching on slot opening (DR-007)
        builder.HasIndex(w => new { w.PreferredDate, w.PreferredProviderId })
            .HasDatabaseName("ix_waitlist_entries_preferred_date_provider");

        // O(1) claim-link resolution (AC-3)
        builder.HasIndex(w => w.ClaimToken)
            .IsUnique()
            .HasFilter("claim_token IS NOT NULL")
            .HasDatabaseName("ix_waitlist_entries_claim_token");

        // ── Foreign key ───────────────────────────────────────────────────────

        builder.HasOne(w => w.Patient)
            .WithMany()
            .HasForeignKey(w => w.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
