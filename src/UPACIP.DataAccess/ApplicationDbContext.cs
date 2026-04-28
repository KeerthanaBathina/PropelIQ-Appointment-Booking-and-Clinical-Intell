using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess;

/// <summary>
/// Application database context for the UPACIP platform.
/// Inherits from <see cref="IdentityDbContext{TUser,TRole,TKey}"/> to co-locate
/// ASP.NET Core Identity tables in the same PostgreSQL schema.
/// </summary>
public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // -------------------------------------------------------------------------
    // Domain DbSets
    // -------------------------------------------------------------------------

    public DbSet<Patient>          Patients          => Set<Patient>();
    public DbSet<Appointment>      Appointments      => Set<Appointment>();
    public DbSet<IntakeData>       IntakeRecords     => Set<IntakeData>();
    public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();
    public DbSet<ExtractedData>    ExtractedData     => Set<ExtractedData>();

    /// <summary>
    /// Per-attempt AI parsing failure records for exponential-backoff retry and terminal-failure audit (US_039 task_004, AC-4, AC-5, EC-1).
    /// </summary>
    public DbSet<DocumentParsingAttempt> DocumentParsingAttempts => Set<DocumentParsingAttempt>();
    public DbSet<MedicalCode>      MedicalCodes      => Set<MedicalCode>();
    public DbSet<AuditLog>         AuditLogs         => Set<AuditLog>();
    public DbSet<QueueEntry>       QueueEntries      => Set<QueueEntry>();
    public DbSet<NotificationLog>  NotificationLogs  => Set<NotificationLog>();

    /// <summary>
    /// Per-attempt audit records for every notification send and orchestration retry (US_037 AC-1, AC-4).
    /// </summary>
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();

    /// <summary>Email verification tokens for the patient registration flow (US_012).</summary>
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    /// <summary>Session audit history for HIPAA compliance (7-year retention, DR-016). US_014.</summary>
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    /// <summary>Password reset tokens for the password-reset flow (US_015, FR-005).</summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    /// <summary>
    /// Provider weekly availability templates used to generate bookable time slots (US_017).
    /// Seed data provides 3 sample providers with Mon–Fri schedules.
    /// </summary>
    public DbSet<ProviderAvailabilityTemplate> ProviderAvailabilityTemplates => Set<ProviderAvailabilityTemplate>();

    /// <summary>Patient waitlist registrations for fully-booked slots (US_020).</summary>
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();

    /// <summary>
    /// Reminder batch checkpoint cursors for 24-hour and 2-hour reminder workers (US_035 EC-1, EC-2).
    /// One row per (BatchType, WindowDateUtc); upserted after each successfully processed appointment.
    /// </summary>
    public DbSet<ReminderBatchCheckpoint> ReminderBatchCheckpoints => Set<ReminderBatchCheckpoint>();

    /// <summary>
    /// Dummy insurance validation reference records used by the soft pre-check
    /// during manual intake (US_031, AC-2, FR-033).
    /// Seeded by the AddMinorGuardianAndInsuranceValidation migration.
    /// </summary>
    public DbSet<InsuranceValidationRecord> InsuranceValidationRecords => Set<InsuranceValidationRecord>();

    /// <summary>
    /// Patient profile consolidation version history (US_043, AC-2, FR-056).
    /// Each row records a consolidation event with timestamp, user attribution,
    /// source document list, and the data delta snapshot.
    /// </summary>
    public DbSet<PatientProfileVersion> PatientProfileVersions => Set<PatientProfileVersion>();

    /// <summary>
    /// Clinical data conflicts detected by the AI conflict detection service (US_044, AC-2, AC-3, AC-5, FR-053).
    /// Each row records a conflict lifecycle from initial detection through staff review to resolution or dismissal.
    /// </summary>
    public DbSet<ClinicalConflict> ClinicalConflicts => Set<ClinicalConflict>();

    /// <summary>
    /// ICD-10 code reference library used for AI-generated diagnosis code validation (US_047, DR-015, FR-063).
    /// Each row represents one code entry in a specific quarterly library version.
    /// Active-code lookups use the composite index on (code_value, is_current).
    /// </summary>
    public DbSet<Icd10CodeLibrary> Icd10CodeLibrary => Set<Icd10CodeLibrary>();

    /// <summary>
    /// CPT procedure code reference library for AI-generated procedure code validation (US_048, AC-4, DR-015, FR-066).
    /// Quarterly refresh sets <c>is_active = false</c> for expired codes.
    /// The EF Core migration and seed data are created by task_003_db_cpt_code_library.
    /// </summary>
    public DbSet<CptCodeLibrary> CptCodeLibrary => Set<CptCodeLibrary>();

    /// <summary>
    /// CPT bundle rule reference table defining which individual CPT codes may be consolidated
    /// into a composite bundle code (US_048 AC-3, task_003_db_cpt_code_library).
    /// Surfaced to staff reviewers when the AI coding pipeline identifies bundling opportunities.
    /// </summary>
    public DbSet<CptBundleRule> CptBundleRules => Set<CptBundleRule>();

    /// <summary>
    /// Immutable append-only audit trail of every staff action taken on a <c>MedicalCode</c> record
    /// (US_049, AC-2, AC-4, FR-066, HIPAA).  Rows must never be updated or deleted.
    /// </summary>
    public DbSet<CodingAuditLog> CodingAuditLogs => Set<CodingAuditLog>();

    /// <summary>
    /// Daily snapshot of AI-human coding agreement rate metrics (US_050, AC-1, AC-2, FR-067).
    /// One row per calendar day, upserted by the agreement-rate calculation job.
    /// </summary>
    public DbSet<AgreementRateMetric> AgreementRateMetrics => Set<AgreementRateMetric>();

    /// <summary>
    /// Immutable records of individual discrepancies between AI-suggested and staff-selected codes
    /// (US_050, FR-068).  Written whenever staff override an AI suggestion.
    /// </summary>
    public DbSet<CodingDiscrepancy> CodingDiscrepancies => Set<CodingDiscrepancy>();

    /// <summary>
    /// Payer-specific and CMS-default code validation rules used by the payer rule
    /// validation service (US_051, AC-1, AC-2, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<PayerRule> PayerRules => Set<PayerRule>();

    /// <summary>
    /// NCCI procedure-to-procedure bundling edits (US_051, AC-4, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<BundlingEdit> BundlingEdits => Set<BundlingEdit>();

    /// <summary>
    /// CPT billing modifier reference data (US_051, AC-4, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<CodeModifier> CodeModifiers => Set<CodeModifier>();

    /// <summary>
    /// Payer rule violations detected during validation runs, with full resolution audit trail
    /// (US_051, AC-2, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<PayerRuleViolation> PayerRuleViolations => Set<PayerRuleViolation>();

    /// <summary>
    /// Immutable queue-specific audit trail recording every priority change and manual reorder
    /// performed by staff (US_054 AC-3, TR-028). Richer than the general AuditLog — includes
    /// original/new position and priority for queue reconstruction and compliance reporting.
    /// Append-only; no update or delete paths are exposed.
    /// </summary>
    public DbSet<QueueAuditLog> QueueAuditLogs => Set<QueueAuditLog>();

    /// <summary>
    /// System-wide key-value configuration table (US_055 AC-3, DR-009).
    /// Stores runtime-configurable platform settings such as the wait time alert threshold
    /// (<c>queue.wait_threshold_minutes</c>). Values are also cached in Redis (60s TTL)
    /// for low-latency reads; this table acts as the source of truth on cache miss.
    /// </summary>
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

    /// <summary>
    /// Pre-aggregated daily queue metrics for history reporting and CSV export (US_056 AC-3, AC-4).
    /// One row per (summary_date, provider_id, appointment_type) partition.
    /// Null provider_id / appointment_type represent all-provider / all-type roll-ups.
    /// </summary>
    public DbSet<QueueDailySummary> QueueDailySummaries => Set<QueueDailySummary>();

    /// <summary>
    /// Pre-aggregated daily system metrics snapshots for the Admin Dashboard trend charts
    /// (US_058 AC-1, AC-2, NFR-004).  One row per UTC calendar day; upserted by the
    /// metrics service or a background aggregation job.
    /// </summary>
    public DbSet<SystemMetricsSnapshot> SystemMetricsSnapshots => Set<SystemMetricsSnapshot>();

    /// <summary>
    /// Notification message template definitions for the Admin Configuration UI
    /// (US_058 AC-3, FR-095).  Each row defines the channel, trigger event, and message body
    /// skeleton for a notification type (e.g. appointment reminder, cancellation notice).
    /// Distinct from <c>NotificationLog</c> which records individual delivery attempts.
    /// </summary>
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    /// <summary>
    /// Singleton risk configuration record including scoring parameter weights
    /// and deferred recalculation flag (US_060 AC-3, AC-4).
    /// The table is designed to hold exactly one row seeded by migration
    /// <c>20260426000003_AddNotificationTemplateAndRiskConfigSchema</c>.
    /// All writes go through the Admin Configuration UI and are protected
    /// by an optimistic-concurrency token.
    /// </summary>
    public DbSet<RiskConfiguration> RiskConfigurations => Set<RiskConfiguration>();

    /// <summary>
    /// Admin-configurable weekly slot template headers — one per (provider, day-of-week)
    /// combination (US_059 AC-1, AC-2).  Child blocks are stored in
    /// <see cref="SlotTemplateBlocks"/>.
    /// </summary>
    public DbSet<SlotTemplate> SlotTemplates => Set<SlotTemplate>();

    /// <summary>
    /// Child time-block records within a <see cref="SlotTemplate"/> defining per-hour
    /// appointment types and availability (US_059 AC-1).
    /// Cascade-deleted with their parent <see cref="SlotTemplate"/>.
    /// </summary>
    public DbSet<SlotTemplateBlock> SlotTemplateBlocks => Set<SlotTemplateBlock>();

    /// <summary>
    /// Clinic-wide business hours — one row per day of the week (US_059 AC-3).
    /// Seeded with Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed.
    /// </summary>
    public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();

    /// <summary>
    /// Holiday definitions that block appointment slots for a specific date (US_059 AC-4).
    /// Supports soft-delete (<c>DeletedAt</c>) and recurring annual holidays.
    /// </summary>
    public DbSet<Holiday> Holidays => Set<Holiday>();

    // ── AI Cost Tracking (US_071) ─────────────────────────────────────────────

    /// <summary>
    /// Immutable per-request AI cost records appended by the AI Gateway after every LLM call
    /// (US_071 TASK_001, AC-1).  Stores provider, request type, token counts, estimated cost,
    /// cost source (actual vs. approximate), and correlation ID for cross-service tracing.
    /// Append-only — no UPDATE or DELETE paths are exposed.
    /// </summary>
    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();

    /// <summary>
    /// Per-provider AI cost budget configuration and rate card (US_071 TASK_001, AC-1, AC-2).
    /// One row per provider (unique constraint on <c>Provider</c>).
    /// Seeded by migration <c>AddAiCostTrackingTables</c> with GPT-4o-mini and Claude 3.5 Sonnet defaults.
    /// </summary>
    public DbSet<AiCostBudgetConfig> AiCostBudgetConfigs => Set<AiCostBudgetConfig>();

    /// <summary>
    /// Pre-aggregated daily AI cost rollup by provider and request type (US_071 TASK_001, AC-1).
    /// Written by the daily aggregation job; unique composite constraint on
    /// (<c>SummaryDate</c>, <c>Provider</c>, <c>RequestType</c>) enables safe upserts.
    /// Used by the admin cost dashboard for O(log n) date-range queries (NFR-004).
    /// </summary>
    public DbSet<AiCostDailySummary> AiCostDailySummaries => Set<AiCostDailySummary>();

    // ── AI Performance Metrics (US_072) ──────────────────────────────────────

    /// <summary>
    /// Daily aggregated AI accuracy metrics (agreement rate, precision, recall) for the
    /// AI monitoring dashboard (US_072 task_001, AC-1, AC-2).
    /// Composite unique constraint on (MetricDate, MetricType) prevents duplicate daily rows.
    /// </summary>
    public DbSet<AiAccuracyMetric> AiAccuracyMetrics => Set<AiAccuracyMetric>();

    /// <summary>
    /// Daily aggregated AI latency percentile metrics (P50, P95 per operation type) for the
    /// AI monitoring dashboard (US_072 task_001, AC-3).
    /// Composite unique constraint on (MetricDate, OperationType) prevents duplicate daily rows.
    /// </summary>
    public DbSet<AiLatencyMetric> AiLatencyMetrics => Set<AiLatencyMetric>();

    /// <summary>
    /// Configurable alert threshold definitions per AI metric name (US_072 task_001, AC-4).
    /// Unique constraint on MetricName enables safe upserts and direct lookup.
    /// </summary>
    public DbSet<AiMetricThreshold> AiMetricThresholds => Set<AiMetricThreshold>();

    /// <summary>
    /// Alert records generated when a daily AI metric calculation drops below its configured
    /// threshold (US_072 task_001, AC-4). Stores metric name, current/target values, and
    /// trend direction. Acknowledged by an admin user via the dashboard.
    /// </summary>
    public DbSet<AiMetricAlert> AiMetricAlerts => Set<AiMetricAlert>();

    // ── Confidence Score Calibration (US_073) ─────────────────────────────────

    /// <summary>
    /// Per-category Platt-scaling calibration parameters used to transform raw AI confidence
    /// scores into calibrated probabilities (US_073 task_001, AC-1).
    /// One active row per <c>DataType</c>; enforced by a unique filtered index on
    /// (<c>DataType</c>, <c>IsActive</c>) WHERE <c>is_active = true</c>.
    /// </summary>
    public DbSet<CalibrationParameter> CalibrationParameters => Set<CalibrationParameter>();

    /// <summary>
    /// Weekly calibration run results storing predicted vs. actual accuracy per confidence bin
    /// per clinical data category (US_073 task_001, AC-3).
    /// One row per (CalibrationRunDate, DataType, BinStart) produced by the weekly calibration job.
    /// </summary>
    public DbSet<CalibrationRecord> CalibrationRecords => Set<CalibrationRecord>();

    /// <summary>
    /// Alert records generated when calibration drift exceeds the 5% threshold between
    /// predicted and actual accuracy for a clinical data category (US_073 task_001, AC-4).
    /// Acknowledged by an admin user; append-only — no update paths are exposed.
    /// </summary>
    public DbSet<CalibrationDriftAlert> CalibrationDriftAlerts => Set<CalibrationDriftAlert>();

    // ── Hallucination Tracking (US_074 task_002) ──────────────────────────────

    /// <summary>
    /// Per-justification staff verification records classifying AI-generated medical
    /// justifications as Supported, Unsupported (hallucination), or PartiallySupported
    /// (US_074 task_002, AC-1, AIR-Q06).
    /// </summary>
    public DbSet<HallucinationRecord> HallucinationRecords => Set<HallucinationRecord>();

    /// <summary>
    /// Pre-aggregated daily hallucination rate metrics (US_074 task_002, AC-1).
    /// One row per calendar day; unique index on MetricDate prevents duplicates.
    /// </summary>
    public DbSet<HallucinationMetric> HallucinationMetrics => Set<HallucinationMetric>();

    /// <summary>
    /// Critical alert records generated when the daily hallucination rate exceeds 5%,
    /// or retroactively when staff discover hallucinations in approved entries (US_074 task_002, AC-2).
    /// Append-only; acknowledged by an admin user via the dashboard.
    /// </summary>
    public DbSet<HallucinationAlert> HallucinationAlerts => Set<HallucinationAlert>();

    // ── AI Audit Logging (US_080 task_002) ───────────────────────────────────

    /// <summary>
    /// Full AI request/response audit trail partitioned by month (US_080 task_002, AIR-S04, AC-3, AC-4).
    /// Each row captures the post-PII-redacted prompt, model response, token counts, latency,
    /// confidence score, request type, and patient/A/B correlation metadata.
    /// EF Core treats the partitioned parent table as a regular table;
    /// PostgreSQL routes inserts to the correct monthly partition transparently.
    /// </summary>
    public DbSet<AiAuditLogEntity> AiAuditLogs => Set<AiAuditLogEntity>();

    // ── A/B Testing (US_080 task_001) ────────────────────────────────────────

    /// <summary>
    /// A/B experiment definitions controlling traffic splitting between AI model versions
    /// (US_080 task_001, AC-1, AIR-O10).
    /// A filtered unique index ensures only one experiment can be Active at a time.
    /// </summary>
    public DbSet<AbExperimentEntity> AbExperiments => Set<AbExperimentEntity>();

    /// <summary>
    /// Per-request AI performance metric records captured by AbTestingMiddleware for each
    /// experiment variant (US_080 task_001, AC-2, AIR-O10).
    /// Append-only — no UPDATE or DELETE paths are exposed.
    /// </summary>
    public DbSet<AbMetricRecordEntity> AbMetricRecords => Set<AbMetricRecordEntity>();

    // ── Uptime Monitoring (US_083 task_001) ───────────────────────────────────

    /// <summary>
    /// Rolling 30-day uptime availability snapshots recorded every 30 seconds by the
    /// uptime monitoring BackgroundService (US_083 task_001, AC-1, NFR-019).
    /// Maintenance-window rows are excluded from SLA computation.
    /// Rows older than 90 days are pruned automatically every 100th probe cycle.
    /// </summary>
    public DbSet<UptimeSnapshot> UptimeSnapshots => Set<UptimeSnapshot>();

    // ── Appointment Archival (US_086 task_002) ─────────────────────────────────

    /// <summary>
    /// Reference stubs retained in the main schema after completed/cancelled appointments
    /// older than the configured threshold are moved to <c>archive.appointments</c>
    /// (US_086 AC-3, AC-5, DR-018, DR-020).
    ///
    /// Each row preserves the original appointment ID, patient ID, and appointment time so
    /// that patient-history queries can render the appointment on the timeline and navigate
    /// to the archive for full details without a full archive-schema scan.
    /// </summary>
    public DbSet<ArchivedAppointmentReference> ArchivedAppointmentReferences => Set<ArchivedAppointmentReference>();

    // ── Patient Archival (US_087 task_002) ─────────────────────────────────────

    /// <summary>
    /// Reference stubs retained in the main schema after soft-deleted patient records
    /// (and all dependent data) are moved to <c>archive.patients</c> (US_087 AC-4, DR-021).
    ///
    /// Each row preserves the original patient ID, name, and email so that audit-log entries
    /// referencing the patient remain resolvable without a cross-schema lookup.
    /// </summary>
    public DbSet<ArchivedPatientReference> ArchivedPatientReferences => Set<ArchivedPatientReference>();

    /// <summary>
    /// Outage lifecycle records created when a dependency transitions Healthy → Unhealthy
    /// and resolved when it recovers (US_083 task_001, AC-3, NFR-019).
    /// Active outages have a null <c>ResolvedAt</c> field.
    /// </summary>
    public DbSet<OutageRecord> OutageRecords => Set<OutageRecord>();

    // ── Database Backup (US_088 task_001) ──────────────────────────────────────

    /// <summary>
    /// Persistent audit log for each automated database backup attempt (US_088, AC-3, DR-022).
    ///
    /// Each row records: filename, size, duration, SHA-256 checksum, status
    /// ("Completed" / "Failed" / "Skipped"), and whether the attempt was a retry (AC-4).
    /// </summary>
    public DbSet<BackupLog> BackupLogs => Set<BackupLog>();

    // ── Backup Restoration Testing (US_089 task_003) ───────────────────────────

    /// <summary>
    /// Persistent audit trail for each admin-triggered quarterly backup restoration test
    /// (US_089 task_003, AC-3, AC-4, DR-026).
    ///
    /// Each row records all three validation outcomes (row counts, FK integrity, checksums),
    /// restoration duration, the admin who triggered the test, and whether it passed overall.
    /// </summary>
    public DbSet<RestorationTestLog> RestorationTestLogs => Set<RestorationTestLog>();

    // ── Point-in-Time Recovery (US_090 task_002) ───────────────────────────────

    /// <summary>
    /// Persistent audit trail for each admin-triggered PITR operation
    /// (US_090 task_002, AC-2, AC-3, DR-027).
    ///
    /// Each row records the target and achieved recovery timestamps, base backup used,
    /// WAL segments replayed, integrity validation outcome, and the admin who triggered it.
    /// </summary>
    public DbSet<RecoveryLog> RecoveryLogs => Set<RecoveryLog>();

    /// <summary>
    /// Persistent audit trail for post-migration integrity verification runs
    /// (US_091 task_002, AC-5, DR-032).
    /// </summary>
    public DbSet<MigrationVerificationLog> MigrationVerificationLogs => Set<MigrationVerificationLog>();

    /// <summary>
    /// Audit trail for all CSV import runs — records file metadata, row counts, and
    /// capped error reports (US_092 task_002, AC-2, AC-3).
    /// </summary>
    public DbSet<ImportLog> ImportLogs => Set<ImportLog>();

    // NOTE: Embedding entity types (MedicalTerminologyEmbedding, IntakeTemplateEmbedding,
    // CodingGuidelineEmbedding) are intentionally excluded from the EF Core model.
    // These tables are provisioned by scripts/provision-pgvector.sql (requires superuser to
    // CREATE EXTENSION vector) and are accessed exclusively via raw NpgsqlCommand in
    // VectorSearchService. The Pgvector.Vector type cannot be mapped by EF Core without
    // pgvector installed on the target PostgreSQL server at design time.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Embedding entity types use Pgvector.Vector which cannot be mapped by EF Core
        // without pgvector installed at design time. Exclude them so migrations and model
        // building succeed on any machine, regardless of pgvector OS installation.
        // VectorSearchService accesses these tables via raw NpgsqlCommand only.
        modelBuilder.Ignore<MedicalTerminologyEmbedding>();
        modelBuilder.Ignore<IntakeTemplateEmbedding>();
        modelBuilder.Ignore<CodingGuidelineEmbedding>();

        // ---------- Identity table name mapping ----------
        // Use asp_net_ prefix + snake_case to follow PostgreSQL naming conventions.
        modelBuilder.Entity<ApplicationUser>()       .ToTable("asp_net_users");
        modelBuilder.Entity<ApplicationRole>()       .ToTable("asp_net_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");

        // ---------- Seed roles ----------
        // Role HasData() is now managed by RoleSeedConfiguration (Seeding/RoleSeedConfiguration.cs),
        // which is auto-discovered below via ApplyConfigurationsFromAssembly.
        // Stable GUIDs: a1b2c3d4... (Patient), b2c3d4e5... (Staff), c3d4e5f6... (Admin).

        // Fluent API entity configurations — auto-discovered from all IEntityTypeConfiguration<T>
        // implementations in this assembly. No manual registration needed when a new configuration
        // class is added to the Configurations/ folder.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}

