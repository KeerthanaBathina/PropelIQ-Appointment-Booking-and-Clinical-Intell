using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class IntakeDataConfiguration : IEntityTypeConfiguration<IntakeData>
{
    public void Configure(EntityTypeBuilder<IntakeData> builder)
    {
        builder.ToTable("intake_data");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.IntakeMethod)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Three independent JSONB columns — each serialized separately by EF Core.
        builder.OwnsOne(i => i.MandatoryFields, owned => owned.ToJson());
        builder.OwnsOne(i => i.OptionalFields,  owned => owned.ToJson());
        builder.OwnsOne(i => i.InsuranceInfo,   owned => owned.ToJson());

        builder.Property(i => i.CompletedAt).IsRequired(false);

        // ── AI conversational intake session state (US_027, EC-2) ──────────────

        // ai_session_id: nullable UUID linking this record to a Redis cache entry.
        // Indexed for fast session-ID lookup (EC-2 deduplication / idempotency).
        builder.Property(i => i.AiSessionId)
            .HasColumnName("ai_session_id")
            .IsRequired(false);

        builder.HasIndex(i => i.AiSessionId)
            .IsUnique(false)
            .HasFilter("ai_session_id IS NOT NULL")
            .HasDatabaseName("ix_intake_data_ai_session_id");

        // ai_session_status: "active" | "summary" | "completed" | "manual".
        // VARCHAR(20) — short enum-like status; not an EF Core enum so legacy rows stay NULL.
        builder.Property(i => i.AiSessionStatus)
            .HasColumnName("ai_session_status")
            .HasMaxLength(20)
            .IsRequired(false);

        // Composite index supporting the active-session resume query pattern (EC-2):
        // WHERE patient_id = @id AND ai_session_status = 'active'
        // ORDER BY last_auto_saved_at DESC LIMIT 1
        builder.HasIndex(i => new { i.PatientId, i.AiSessionStatus })
            .HasFilter("ai_session_status IS NOT NULL")
            .HasDatabaseName("ix_intake_data_patient_ai_status");

        // last_auto_saved_at: UTC timestamp of most recent autosave (EC-2 tiebreaker).
        builder.Property(i => i.LastAutoSavedAt)
            .HasColumnName("last_auto_saved_at")
            .IsRequired(false);

        // ai_session_snapshot: full session state as JSONB.
        // Contains collected fields, current question key, and turn count (EC-2 restore).
        builder.OwnsOne(i => i.AiSessionSnapshot, snapshot =>
        {
            snapshot.ToJson("ai_session_snapshot");

            snapshot.OwnsMany(s => s.CollectedFields, field =>
            {
                field.Property(f => f.Key);
                field.Property(f => f.Value);
            });
        });

        // ── Mode-switch attribution (US_029, AC-3, AC-4, EC-1, EC-2) ───────────
        // intake_attribution: per-field source provenance + conflict audit log + switch history.
        // Stored as a single JSONB column — NULL when no mode switch has occurred.
        // Three nested JSON arrays: field_attributions, conflict_notes, mode_switch_events.
        builder.OwnsOne(i => i.AttributionSnapshot, snapshot =>
        {
            snapshot.ToJson("intake_attribution");

            // Per-field source attribution (which mode provided the accepted value)
            snapshot.OwnsMany(s => s.FieldAttributions, fa =>
            {
                fa.Property(f => f.FieldKey);
                fa.Property(f => f.Source);
                fa.Property(f => f.CollectedAt);
            });

            // Conflict audit log (superseded values retained for provenance — EC-1)
            snapshot.OwnsMany(s => s.ConflictNotes, cn =>
            {
                cn.Property(c => c.FieldKey);
                cn.Property(c => c.WinningValue);
                cn.Property(c => c.WinningSource);
                cn.Property(c => c.ReplacedValue);
                cn.Property(c => c.ReplacedSource);
                cn.Property(c => c.RecordedAt);
            });

            // Mode-switch event log (ordered history of AI ↔ manual transitions — EC-2)
            snapshot.OwnsMany(s => s.ModeSwitchEvents, ev =>
            {
                ev.Property(e => e.FromMode);
                ev.Property(e => e.ToMode);
                ev.Property(e => e.SwitchedAt);
                ev.Property(e => e.CorrelationId);
            });
        });

        builder.HasIndex(i => i.PatientId)
            .HasDatabaseName("ix_intake_data_patient_id");

        // ── Minor guardian consent (US_031, AC-1, EC-1) ───────────────────────
        // guardian_consent: JSONB NULL — captures guardian identity, relationship, and
        // consent acknowledgment.  NULL for adult patients and legacy rows.
        builder.OwnsOne(i => i.GuardianConsent, gc =>
        {
            gc.ToJson("guardian_consent");
            gc.Property(g => g.GuardianName).IsRequired(false);
            gc.Property(g => g.GuardianDateOfBirth).IsRequired(false);
            gc.Property(g => g.GuardianRelationship).IsRequired(false);
            gc.Property(g => g.ConsentAcknowledged);
            gc.Property(g => g.ConsentRecordedAt).IsRequired(false);
        });

        // ── Insurance soft pre-check outcome (US_031, AC-2, AC-3, EC-2) ────────
        // Scalar columns — not JSONB — so the staff-follow-up partial index uses a
        // simple boolean filter rather than an expensive JSONB path expression.

        builder.Property(i => i.InsuranceValidationStatus)
            .HasColumnName("insurance_validation_status")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(i => i.InsuranceReviewReason)
            .HasColumnName("insurance_review_reason")
            .IsRequired(false);

        // insurance_requires_staff_followup: defaults to false for backward compatibility.
        builder.Property(i => i.InsuranceRequiresStaffFollowup)
            .HasColumnName("insurance_requires_staff_followup")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(i => i.InsuranceValidatedAt)
            .HasColumnName("insurance_validated_at")
            .IsRequired(false);

        // Partial index supporting staff-dashboard queries: completed intake rows that require
        // insurance follow-up (AC-3).  Excludes the vast majority of adult / valid-insurance rows.
        builder.HasIndex(i => new { i.PatientId, i.InsuranceRequiresStaffFollowup })
            .HasFilter("insurance_requires_staff_followup = true AND completed_at IS NOT NULL")
            .HasDatabaseName("ix_intake_data_insurance_staff_followup");

        builder.HasOne(i => i.Patient)
            .WithMany(p => p.IntakeRecords)
            .HasForeignKey(i => i.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
