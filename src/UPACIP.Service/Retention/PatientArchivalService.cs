using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Retention.Models;

namespace UPACIP.Service.Retention;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Moves soft-deleted patient records (and all dependent data) to the PostgreSQL
/// <c>archive</c> schema when the soft-delete timestamp exceeds the configured
/// <see cref="RetentionPolicyOptions.SoftDeletedPatientArchivalDays"/> threshold
/// (US_087 AC-4, DR-021).
///
/// This is a <b>cascading operation</b>: for each eligible patient, the following
/// tables are archived and then deleted from the main schema in a single transaction:
/// <c>extracted_data</c> → <c>clinical_documents</c> → <c>medical_codes</c> →
/// <c>intake_data</c> → <c>appointments</c> (already archived by US_086) → <c>patients</c>.
///
/// An <see cref="ArchivedPatientReference"/> stub is retained in the main schema
/// with the original patient ID, name, and email so that audit-log entries
/// referencing the patient remain resolvable (US_064, HIPAA).
///
/// Clinical records (ClinicalDocument, ExtractedData, MedicalCode) are archived to
/// cold storage rather than deleted, satisfying DR-017 indefinite retention.
/// Archival is not deletion; the <see cref="IRetentionPolicyGuard.CanDeleteAsync"/>
/// guard for <see cref="RetentionCategory.ClinicalRecords"/> does not apply.
/// </summary>
public interface IPatientArchivalService
{
    /// <summary>
    /// Archives all soft-deleted patients whose <c>DeletedAt</c> timestamp is older
    /// than <see cref="RetentionPolicyOptions.SoftDeletedPatientArchivalDays"/> days ago.
    /// </summary>
    Task<ArchivalResult> ArchiveSoftDeletedPatientsAsync(CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scoped implementation of <see cref="IPatientArchivalService"/>.
///
/// Archival loop (per candidate patient):
/// <list type="number">
///   <item>Load a batch of eligible soft-deleted patients (minimal projection, IgnoreQueryFilters).</item>
///   <item>Skip if patient is referenced by an audit log within the 7-year window (edge case 2).</item>
///   <item>Safety-check: skip if any active (Scheduled) appointments remain (should not occur
///         because task_001 blocked the original soft-delete, but guards against data inconsistency).</item>
///   <item>Within a transaction per patient:
///     <list type="bullet">
///       <item>Archive <c>extracted_data</c> rows (via document FK) → <c>archive.extracted_data</c>.</item>
///       <item>Archive <c>clinical_documents</c> → <c>archive.clinical_documents</c>.</item>
///       <item>Archive <c>medical_codes</c> → <c>archive.medical_codes</c>.</item>
///       <item>Archive <c>intake_data</c> → <c>archive.intake_data</c>.</item>
///       <item>Archive the patient row → <c>archive.patients</c>.</item>
///       <item>Create an <see cref="ArchivedPatientReference"/> stub.</item>
///       <item>Delete from main tables bottom-up (extracted_data → clinical_documents
///             → medical_codes → intake_data → appointments → patient).</item>
///     </list>
///   </item>
///   <item>On transaction failure: log error and continue to next patient.</item>
/// </list>
///
/// Notification logs are not separately handled because US_086 task_001 purges them
/// at 90 days — well before the 365-day patient archival threshold.  The appointment
/// cascade in <c>OnDelete(Cascade)</c> would normally remove related notification logs,
/// but since appointments are already archived (and their notification logs already purged)
/// by the time patient archival runs, this is a no-op in practice.
/// </summary>
public sealed class PatientArchivalService : IPatientArchivalService
{
    private readonly ApplicationDbContext                    _db;
    private readonly IRetentionPolicyGuard                   _guard;
    private readonly IOptionsMonitor<RetentionPolicyOptions> _options;
    private readonly ILogger<PatientArchivalService>         _logger;

    public PatientArchivalService(
        ApplicationDbContext                    db,
        IRetentionPolicyGuard                   guard,
        IOptionsMonitor<RetentionPolicyOptions> options,
        ILogger<PatientArchivalService>         logger)
    {
        _db      = db;
        _guard   = guard;
        _options = options;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public interface
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ArchivalResult> ArchiveSoftDeletedPatientsAsync(CancellationToken ct = default)
    {
        var opts   = _options.CurrentValue;
        var cutoff = DateTime.UtcNow.AddDays(-opts.SoftDeletedPatientArchivalDays);
        var sw     = Stopwatch.StartNew();

        _logger.LogInformation(
            "PatientArchivalService: starting soft-deleted patient archival. " +
            "Cutoff={Cutoff:o} (SoftDeletedPatientArchivalDays={Days})",
            cutoff, opts.SoftDeletedPatientArchivalDays);

        int totalArchived     = 0;
        int totalAuditSkipped = 0;
        int totalSafetySkipped = 0;
        DateTime? oldestDate  = null;
        DateTime? newestDate  = null;

        bool hasMore = true;
        while (hasMore && !ct.IsCancellationRequested)
        {
            // ── Load next batch of soft-deleted patients past the archival threshold ──
            // IgnoreQueryFilters is mandatory — soft-deleted rows are hidden from all
            // standard queries.  We explicitly filter to rows where DeletedAt is not null.
            var candidates = await _db.Patients
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.DeletedAt != null && p.DeletedAt < cutoff)
                .OrderBy(p => p.DeletedAt)      // oldest-first for visible progress
                .Select(p => new
                {
                    p.Id,
                    p.FullName,
                    p.Email,
                    DeletedAtUtc = p.DeletedAt!.Value,
                    p.CreatedAt,
                })
                .Take(opts.PurgeBatchSize)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                hasMore = false;
                break;
            }

            int batchArchived = 0;

            foreach (var candidate in candidates)
            {
                if (ct.IsCancellationRequested) break;

                // ── Audit-log reference protection (edge case 2) ──────────────────
                if (opts.EnforceAuditLogReferenceProtection)
                {
                    bool auditProtected = await _guard.IsProtectedByAuditLogAsync(
                        "Patient", candidate.Id, ct);

                    if (auditProtected)
                    {
                        _logger.LogDebug(
                            "PATIENT_ARCHIVAL_DEFERRED: PatientId={Id} is referenced by an audit " +
                            "log entry within the {Years}-year window. Skipping.",
                            candidate.Id, opts.AuditLogRetentionYears);
                        totalAuditSkipped++;
                        continue;
                    }
                }

                // ── Safety check: active appointments (should not exist — task_001 guards this) ──
                var activeAppointments = await _db.Appointments
                    .CountAsync(a => a.PatientId == candidate.Id
                                  && a.Status == AppointmentStatus.Scheduled, ct);

                if (activeAppointments > 0)
                {
                    _logger.LogWarning(
                        "PATIENT_ARCHIVAL_DEFERRED: PatientId={Id} has {Count} active scheduled " +
                        "appointment(s) despite being soft-deleted. Skipping until resolved.",
                        candidate.Id, activeAppointments);
                    totalSafetySkipped++;
                    continue;
                }

                // ── Archive the patient and all dependent data ────────────────────
                bool archived = await ArchiveSinglePatientAsync(
                    candidate.Id,
                    candidate.FullName,
                    candidate.Email,
                    candidate.DeletedAtUtc,
                    ct);

                if (archived)
                {
                    batchArchived++;
                    totalArchived++;

                    if (oldestDate is null || candidate.DeletedAtUtc < oldestDate)
                        oldestDate = candidate.DeletedAtUtc;
                    if (newestDate is null || candidate.DeletedAtUtc > newestDate)
                        newestDate = candidate.DeletedAtUtc;
                }
            }

            _logger.LogDebug(
                "PatientArchivalService: batch complete — archived {Count} patient(s).",
                batchArchived);

            // Stop if nothing was archivable in this batch to prevent an infinite loop
            // over audit-protected or safety-skipped patients.
            hasMore = candidates.Count == opts.PurgeBatchSize && batchArchived > 0;
        }

        sw.Stop();

        var result = new ArchivalResult
        {
            Category                     = RetentionCategory.ClinicalRecords,
            RecordsArchived              = totalArchived,
            RecordsSkippedAuditProtected = totalAuditSkipped,
            RecordsSkippedActiveFk       = totalSafetySkipped,
            OldestArchivedDate           = oldestDate,
            NewestArchivedDate           = newestDate,
            ExecutionDuration            = sw.Elapsed,
        };

        EmitSummary(result);
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Single-patient cascading archival (transactional)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> ArchiveSinglePatientAsync(
        Guid              patientId,
        string            fullName,
        string            email,
        DateTime          deletedAtUtc,
        CancellationToken ct)
    {
        var archivedAt = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // ── Step 1: Archive extracted_data (leaves via clinical_documents FK) ───
            // ON CONFLICT DO NOTHING for idempotency — safe to re-run on partial failure.
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO archive.extracted_data
                SELECT e.*, {archivedAt} AS archived_at_utc
                FROM   extracted_data e
                       JOIN clinical_documents cd ON cd."Id" = e."DocumentId"
                WHERE  cd."PatientId" = {patientId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 2: Archive clinical_documents ───────────────────────────────
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO archive.clinical_documents
                SELECT *, {archivedAt} AS archived_at_utc
                FROM   clinical_documents
                WHERE  "PatientId" = {patientId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 3: Archive medical_codes ────────────────────────────────────
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO archive.medical_codes
                SELECT *, {archivedAt} AS archived_at_utc
                FROM   medical_codes
                WHERE  "PatientId" = {patientId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 4: Archive intake_data ──────────────────────────────────────
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO archive.intake_data
                SELECT *, {archivedAt} AS archived_at_utc
                FROM   intake_data
                WHERE  "PatientId" = {patientId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 5: Archive appointments not already in archive ──────────────
            // US_086 task_002 archives completed/cancelled appointments on their own
            // schedule. Any remaining appointments (e.g. NoShow) are archived here.
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO archive.appointments
                    ("Id", "PatientId", "BookingReference", "AppointmentTime", "Status",
                     "IsWalkIn", "ProviderId", "ProviderName", "AppointmentType",
                     "PreferredSlotCriteria", "Version",
                     no_show_risk_score, no_show_risk_band, is_risk_estimated,
                     requires_outreach, risk_calculated_at_utc,
                     "SlotTemplateId", "CreatedAt", "UpdatedAt", archived_at_utc)
                SELECT
                     "Id", "PatientId", "BookingReference", "AppointmentTime", "Status",
                     "IsWalkIn", "ProviderId", "ProviderName", "AppointmentType",
                     "PreferredSlotCriteria", "Version",
                     no_show_risk_score, no_show_risk_band, is_risk_estimated,
                     requires_outreach, risk_calculated_at_utc,
                     "SlotTemplateId", "CreatedAt", "UpdatedAt", {archivedAt}
                FROM   appointments
                WHERE  "PatientId" = {patientId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 6: Archive the patient row itself ───────────────────────────
            await _db.Database.ExecuteSqlAsync($"""
                INSERT INTO archive.patients
                SELECT *, {archivedAt} AS archived_at_utc
                FROM   patients
                WHERE  "Id" = {patientId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 7: Create ArchivedPatientReference stub ─────────────────────
            bool refExists = await _db.ArchivedPatientReferences
                .AnyAsync(r => r.Id == patientId, ct);

            if (!refExists)
            {
                _db.ArchivedPatientReferences.Add(new ArchivedPatientReference
                {
                    Id            = patientId,
                    FullName      = fullName,
                    Email         = email,
                    DeletedAtUtc  = deletedAtUtc,
                    ArchivedAtUtc = archivedAt,
                    ArchiveSchema = "archive",
                });
                await _db.SaveChangesAsync(ct);
            }

            // ── Step 8: Delete from main tables bottom-up (FK order) ─────────────
            // extracted_data → clinical_documents → (archived_appointment_references if any)
            // → medical_codes → intake_data → appointments → patient.

            // extracted_data: references clinical_documents
            await _db.ExtractedData
                .Where(e => _db.ClinicalDocuments
                    .Where(cd => cd.PatientId == patientId)
                    .Select(cd => cd.Id)
                    .Contains(e.DocumentId))
                .ExecuteDeleteAsync(ct);

            // clinical_documents: references patient
            await _db.ClinicalDocuments
                .Where(d => d.PatientId == patientId)
                .ExecuteDeleteAsync(ct);

            // medical_codes: references patient
            await _db.MedicalCodes
                .Where(m => m.PatientId == patientId)
                .ExecuteDeleteAsync(ct);

            // intake_data: references patient
            await _db.IntakeRecords
                .Where(i => i.PatientId == patientId)
                .ExecuteDeleteAsync(ct);

            // archived_appointment_references: stubs from US_086 task_002
            await _db.ArchivedAppointmentReferences
                .Where(r => r.PatientId == patientId)
                .ExecuteDeleteAsync(ct);

            // appointments: references patient (cascade handles QueueEntries)
            await _db.Appointments
                .IgnoreQueryFilters()
                .Where(a => a.PatientId == patientId)
                .ExecuteDeleteAsync(ct);

            // patients: the root row — IgnoreQueryFilters required (DeletedAt != null)
            await _db.Patients
                .IgnoreQueryFilters()
                .Where(p => p.Id == patientId)
                .ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogDebug(
                "PatientArchivalService: archived patient {Id} ({Email}) → archive.patients. " +
                "ArchivedAt={ArchivedAt:o}",
                patientId, email, archivedAt);

            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            _logger.LogError(ex,
                "PatientArchivalService: failed to archive patient {Id}. " +
                "Transaction rolled back. Will retry on next nightly cycle.",
                patientId);

            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Logging
    // ─────────────────────────────────────────────────────────────────────────

    private void EmitSummary(ArchivalResult result)
    {
        if (result.RecordsArchived == 0
            && result.RecordsSkippedAuditProtected == 0
            && result.RecordsSkippedActiveFk == 0)
        {
            _logger.LogDebug(
                "RETENTION_PATIENT_ARCHIVAL_COMPLETE: Archived=0, Duration={DurationMs}ms. " +
                "No patients eligible for archival.",
                (int)result.ExecutionDuration.TotalMilliseconds);
            return;
        }

        _logger.LogInformation(
            "RETENTION_PATIENT_ARCHIVAL_COMPLETE: Archived={Count}, " +
            "Skipped(AuditProtected)={AuditSkipped}, Skipped(SafetyCheck)={SafetySkipped}, " +
            "OldestArchivedDeletedAt={OldestDate:o}, NewestArchivedDeletedAt={NewestDate:o}, " +
            "Duration={DurationMs}ms",
            result.RecordsArchived,
            result.RecordsSkippedAuditProtected,
            result.RecordsSkippedActiveFk,
            result.OldestArchivedDate,
            result.NewestArchivedDate,
            (int)result.ExecutionDuration.TotalMilliseconds);
    }
}
