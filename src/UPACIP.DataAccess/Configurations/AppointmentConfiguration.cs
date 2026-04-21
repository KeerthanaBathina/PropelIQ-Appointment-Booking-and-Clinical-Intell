using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        // Enum stored as string to keep data human-readable in PostgreSQL.
        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Optimistic-concurrency token — EF Core includes Version in every UPDATE WHERE clause.
        // If the row has been modified since the entity was loaded, DbUpdateConcurrencyException is thrown.
        builder.Property(a => a.Version)
            .IsConcurrencyToken();

        // JSONB column — EF Core 7+ ToJson() serializes the owned type as a single JSONB cell.
        builder.OwnsOne(a => a.PreferredSlotCriteria, owned =>
        {
            owned.ToJson();
        });

        // Provider fields (US_017) — nullable to remain compatible with pre-US_017 records
        builder.Property(a => a.ProviderId)
            .IsRequired(false);

        builder.Property(a => a.ProviderName)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(a => a.AppointmentType)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.HasIndex(a => a.PatientId)
            .HasDatabaseName("ix_appointments_patient_id");

        builder.HasIndex(a => a.AppointmentTime)
            .HasDatabaseName("ix_appointments_appointment_time");

        // Composite index (US_017 AC-1): supports the slot availability query pattern
        //   WHERE appointment_time BETWEEN @from AND @to AND status != 'Cancelled'
        // The leading column is appointment_time so PostgreSQL can range-scan within
        // a date window, then the status predicate is evaluated in-index (covering).
        builder.HasIndex(a => new { a.AppointmentTime, a.Status })
            .HasDatabaseName("ix_appointments_appointment_time_status");

        // Composite index (US_017 AC-1): extends the above index with provider_id to avoid
        // a heap fetch when filtering by provider:
        //   WHERE appointment_time BETWEEN @from AND @to
        //     AND status != 'Cancelled'
        //     AND provider_id = @providerId
        builder.HasIndex(a => new { a.AppointmentTime, a.Status, a.ProviderId })
            .HasDatabaseName("ix_appointments_appointment_time_status_provider_id");

        // Composite unique constraint (DR-014): prevents duplicate bookings for the same
        // patient at the same time slot. PostgreSQL will reject any INSERT/UPDATE that
        // duplicates (patient_id, appointment_time) with SqlState = "23505" (unique_violation).
        // EF Core surfaces this as DbUpdateException containing PostgresException.
        builder.HasIndex(a => new { a.PatientId, a.AppointmentTime })
            .IsUnique()
            .HasDatabaseName("ix_appointments_patient_id_appointment_time");

        // FK is configured on PatientConfiguration (one-to-many); only navigation registered here.
        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.QueueEntry)
            .WithOne(q => q.Appointment)
            .HasForeignKey<QueueEntry>(q => q.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Notifications)
            .WithOne(n => n.Appointment)
            .HasForeignKey(n => n.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── No-show risk metadata columns (US_026, AIR-006, FR-014) ──────────
        // All nullable: existing appointments have no score until their first scoring run.

        // Score is constrained to [0, 100] via a DB CHECK constraint (EC-2, DR guardrails).
        // The CHECK is added in the migration rather than here to avoid changing the ToTable()
        // call while keeping the constraint visible in the migration diff history.
        builder.Property(a => a.NoShowRiskScore)
            .HasColumnType("integer")
            .IsRequired(false);

        // Band stored as string for human-readable DB values (same pattern as Status column).
        builder.Property(a => a.NoShowRiskBand)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired(false);

        builder.Property(a => a.IsRiskEstimated)
            .HasColumnType("boolean")
            .IsRequired(false);

        builder.Property(a => a.RequiresOutreach)
            .HasColumnType("boolean")
            .IsRequired(false);

        builder.Property(a => a.RiskCalculatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // Composite index: supports staff schedule and queue queries sorted by risk
        //   WHERE status = 'Scheduled' ORDER BY no_show_risk_score DESC
        // Leading column is no_show_risk_score so the planner can use index-only scan
        // for the highest-risk filter (requires_outreach = true) on that same column.
        builder.HasIndex(a => new { a.NoShowRiskScore, a.Status })
            .HasDatabaseName("ix_appointments_no_show_risk_score_status");
    }
}
