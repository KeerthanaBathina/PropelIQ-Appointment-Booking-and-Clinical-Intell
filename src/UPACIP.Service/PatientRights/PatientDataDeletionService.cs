using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.PatientRights.Deletion;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights;

/// <summary>
/// HIPAA Right to Deletion patient data deletion service (US_094, NFR-045, AC-3, AC-4).
///
/// Six-phase pipeline:
///   Phase 1 — Cancel all pending (Scheduled) appointments.
///   Phase 2 — Anonymize shared/consolidated clinical data.
///   Phase 3 — Hard-delete all dependent entities in FK-safe order.
///   Phase 4 — Soft-delete the patient row and wipe PII fields.
///   Phase 5 — Purge Redis cache keys and document files from disk.
///   Phase 6 — Anonymize audit log references (user IDs / addresses).
/// </summary>
public sealed class PatientDataDeletionService : IPatientDataDeletionService
{
    private const int SlaDeadlineDays = 30;

    private readonly ApplicationDbContext             _db;
    private readonly PendingAppointmentHandler        _appointmentHandler;
    private readonly SharedDataAnonymizer             _sharedAnonymizer;
    private readonly CacheCleanupService              _cacheCleanup;
    private readonly FileCleanupService               _fileCleanup;
    private readonly DeletionVerificationService      _verificationService;
    private readonly IAuditLogService                 _auditLog;
    private readonly ILogger<PatientDataDeletionService> _logger;

    public PatientDataDeletionService(
        ApplicationDbContext                 db,
        PendingAppointmentHandler            appointmentHandler,
        SharedDataAnonymizer                 sharedAnonymizer,
        CacheCleanupService                  cacheCleanup,
        FileCleanupService                   fileCleanup,
        DeletionVerificationService          verificationService,
        IAuditLogService                     auditLog,
        ILogger<PatientDataDeletionService>  logger)
    {
        _db                  = db;
        _appointmentHandler  = appointmentHandler;
        _sharedAnonymizer    = sharedAnonymizer;
        _cacheCleanup        = cacheCleanup;
        _fileCleanup         = fileCleanup;
        _verificationService = verificationService;
        _auditLog            = auditLog;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Submit
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DataAccessRequest> SubmitDeletionRequestAsync(
        Guid patientId, string requestedBy, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var request = new DataAccessRequest
        {
            Id             = Guid.NewGuid(),
            PatientId      = patientId,
            RequestType    = "DataDeletion",
            Status         = "Submitted",
            RequestedAtUtc = now,
            DeadlineUtc    = now.AddDays(SlaDeadlineDays),
            RequestedBy    = requestedBy,
            CreatedAt      = now,
            UpdatedAt      = now,
        };

        _db.DataAccessRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DELETION_REQUEST_SUBMITTED: RequestId={RequestId}, PatientId={PatientId}",
            request.Id, patientId);

        try
        {
            await _auditLog.LogAsync(
                AuditAction.DataDelete,
                userId:       null,
                resourceType: "Patient",
                ipAddress:    string.Empty,
                userAgent:    string.Empty,
                resourceId:   patientId,
                cancellationToken: ct,
                systemEvent:  true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DELETION_AUDIT_LOG_FAILED: RequestId={RequestId}. Continuing.", request.Id);
        }

        return request;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Process
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DeletionResult> ProcessDeletionAsync(
        Guid requestId, string processedBy, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var warnings  = new List<string>();

        var request = await _db.DataAccessRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RequestType == "DataDeletion", ct)
            ?? throw new KeyNotFoundException($"DataDeletion request {requestId} not found.");

        if (request.Status == "Completed")
            throw new InvalidOperationException(
                $"Deletion request {requestId} has already been completed.");

        var patientId = request.PatientId;

        // Mark in-progress.
        request.Status      = "Processing";
        request.ProcessedBy = processedBy;
        request.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            // ── Phase 1: Cancel pending appointments ─────────────────────────────
            _logger.LogInformation(
                "DELETION_PHASE1_START: PatientId={PatientId}, Phase=CancelAppointments", patientId);
            var appointmentsCancelled = await _appointmentHandler.CancelPendingAppointmentsAsync(patientId, ct);

            // ── Phase 2: Anonymize shared data ───────────────────────────────────
            _logger.LogInformation(
                "DELETION_PHASE2_START: PatientId={PatientId}, Phase=AnonymizeSharedData", patientId);
            var recordsAnonymized = await _sharedAnonymizer.AnonymizeSharedDataAsync(patientId, ct);

            // ── Phase 3: Hard-delete entities in FK-safe order ───────────────────
            _logger.LogInformation(
                "DELETION_PHASE3_START: PatientId={PatientId}, Phase=HardDeleteEntities", patientId);
            var entitiesDeleted = await HardDeleteEntitiesAsync(patientId, ct);

            // ── Phase 4: Soft-delete patient row and wipe PII ────────────────────
            _logger.LogInformation(
                "DELETION_PHASE4_START: PatientId={PatientId}, Phase=SoftDeletePatient", patientId);
            var patientSoftDeleted = await SoftDeletePatientAsync(patientId, ct);

            // ── Phase 5: Purge cache and files ───────────────────────────────────
            _logger.LogInformation(
                "DELETION_PHASE5_START: PatientId={PatientId}, Phase=PurgeCacheAndFiles", patientId);
            var cacheKeysDeleted = await _cacheCleanup.PurgePatientCacheAsync(patientId, ct);
            var filesDeleted     = await _fileCleanup.DeletePatientFilesAsync(patientId, ct);

            // ── Phase 6: Anonymize audit log references ──────────────────────────
            _logger.LogInformation(
                "DELETION_PHASE6_START: PatientId={PatientId}, Phase=AnonymizeAuditLogs", patientId);
            var auditLogsAnonymized = await AnonymizeAuditLogsAsync(patientId, ct, warnings);

            // ── Post-deletion verification ───────────────────────────────────────
            var verification = await _verificationService.VerifyDeletionAsync(patientId, ct);
            if (!verification.FullyDeleted)
            {
                warnings.AddRange(verification.RemainingDataLocations
                    .Select(loc => $"Data remains at: {loc}"));
            }

            // ── Mark completed ───────────────────────────────────────────────────
            var finalStatus = warnings.Count > 0 ? "CompletedWithWarnings" : "Completed";
            request.Status         = finalStatus;
            request.CompletedAtUtc = DateTime.UtcNow;
            request.UpdatedAt      = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "DELETION_COMPLETE: RequestId={RequestId}, PatientId={PatientId}, " +
                "Status={Status}, Duration={Duration}",
                requestId, patientId, finalStatus, DateTime.UtcNow - startedAt);

            return new DeletionResult
            {
                RequestId            = requestId,
                PatientId            = patientId,
                Status               = finalStatus,
                AppointmentsCancelled= appointmentsCancelled,
                RecordsAnonymized    = recordsAnonymized,
                EntitiesDeleted      = entitiesDeleted,
                PatientSoftDeleted   = patientSoftDeleted,
                CacheKeysDeleted     = cacheKeysDeleted,
                FilesDeleted         = filesDeleted,
                AuditLogsAnonymized  = auditLogsAnonymized,
                Verification         = verification,
                Warnings             = warnings,
                Duration             = DateTime.UtcNow - startedAt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DELETION_FAILED: RequestId={RequestId}, PatientId={PatientId}",
                requestId, patientId);

            request.Status        = "Failed";
            request.FailureReason = ex.Message;
            request.UpdatedAt     = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, int>> HardDeleteEntitiesAsync(
        Guid patientId, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>();

        // Collect the appointment IDs once — used by three dependent tables.
        var appointmentIds = await _db.Appointments
            .Where(a => a.PatientId == patientId)
            .Select(a => a.Id)
            .ToListAsync(ct);

        // 1. NotificationDeliveryAttempts — depends on NotificationLogs
        if (appointmentIds.Count > 0)
        {
            var notifIds = await _db.NotificationLogs
                .Where(n => appointmentIds.Contains(n.AppointmentId))
                .Select(n => n.NotificationId)
                .ToListAsync(ct);

            if (notifIds.Count > 0)
            {
                var deliveryAttempts = await _db.NotificationDeliveryAttempts
                    .Where(d => notifIds.Contains(d.NotificationId))
                    .ToListAsync(ct);
                _db.NotificationDeliveryAttempts.RemoveRange(deliveryAttempts);
                await _db.SaveChangesAsync(ct);
                counts["NotificationDeliveryAttempts"] = deliveryAttempts.Count;
            }
        }

        // 2. NotificationLogs — depends on Appointments
        if (appointmentIds.Count > 0)
        {
            var notifLogs = await _db.NotificationLogs
                .Where(n => appointmentIds.Contains(n.AppointmentId))
                .ToListAsync(ct);
            _db.NotificationLogs.RemoveRange(notifLogs);
            await _db.SaveChangesAsync(ct);
            counts["NotificationLogs"] = notifLogs.Count;
        }

        // 3. QueueEntries — depends on Appointments
        if (appointmentIds.Count > 0)
        {
            var queueEntries = await _db.QueueEntries
                .Where(q => appointmentIds.Contains(q.AppointmentId))
                .ToListAsync(ct);
            _db.QueueEntries.RemoveRange(queueEntries);
            await _db.SaveChangesAsync(ct);
            counts["QueueEntries"] = queueEntries.Count;
        }

        // 4. ExtractedData for patient-only documents (staff-docs were anonymized in Phase 2)
        var patientDocIds = await _db.ClinicalDocuments
            .Where(d => d.PatientId == patientId)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (patientDocIds.Count > 0)
        {
            var extractions = await _db.ExtractedData
                .Where(e => patientDocIds.Contains(e.DocumentId))
                .ToListAsync(ct);
            _db.ExtractedData.RemoveRange(extractions);
            await _db.SaveChangesAsync(ct);
            counts["ExtractedData"] = extractions.Count;
        }

        // 5. MedicalCodes not yet anonymized (patient-only codes without staff approval)
        var medicalCodes = await _db.MedicalCodes
            .Where(m => m.PatientId == patientId)
            .ToListAsync(ct);
        _db.MedicalCodes.RemoveRange(medicalCodes);
        await _db.SaveChangesAsync(ct);
        counts["MedicalCodes"] = medicalCodes.Count;

        // 6. ClinicalDocuments
        var clinicalDocs = await _db.ClinicalDocuments
            .Where(d => d.PatientId == patientId)
            .ToListAsync(ct);
        _db.ClinicalDocuments.RemoveRange(clinicalDocs);
        await _db.SaveChangesAsync(ct);
        counts["ClinicalDocuments"] = clinicalDocs.Count;

        // 7. IntakeData
        var intakeData = await _db.IntakeRecords
            .Where(i => i.PatientId == patientId)
            .ToListAsync(ct);
        _db.IntakeRecords.RemoveRange(intakeData);
        await _db.SaveChangesAsync(ct);
        counts["IntakeData"] = intakeData.Count;

        // 8. Appointments
        var appointments = await _db.Appointments
            .Where(a => a.PatientId == patientId)
            .ToListAsync(ct);
        _db.Appointments.RemoveRange(appointments);
        await _db.SaveChangesAsync(ct);
        counts["Appointments"] = appointments.Count;

        _logger.LogInformation(
            "DELETION_PHASE3_COMPLETE: PatientId={PatientId}, Counts={@Counts}",
            patientId, counts);

        return counts;
    }

    private async Task<bool> SoftDeletePatientAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null)
        {
            _logger.LogWarning(
                "DELETION_PHASE4_PATIENT_NOT_FOUND: PatientId={PatientId}", patientId);
            return false;
        }

        var now = DateTime.UtcNow;

        // Wipe all PII fields (GDPR/HIPAA compliance).
        patient.Email            = $"deleted_{patientId}@removed.local";
        patient.FullName         = "[DELETED]";
        patient.PhoneNumber      = string.Empty;
        patient.EmergencyContact = null;
        patient.PasswordHash     = string.Empty;
        patient.DeletedAt        = now;
        patient.UpdatedAt        = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DELETION_PHASE4_COMPLETE: PatientId={PatientId} soft-deleted and PII wiped.", patientId);

        return true;
    }

    private async Task<bool> AnonymizeAuditLogsAsync(
        Guid patientId, CancellationToken ct, List<string> warnings)
    {
        // Audit log rows are immutable by policy (DR-016, append-only).
        // We anonymize the UserId reference on entries whose UserId corresponds to the
        // patient's own user account (ResourceId = patientId entries by the patient themselves).
        // The ResourceId is retained so the log remains navigable by admins.
        //
        // This is a best-effort operation — failure is non-fatal.
        try
        {
            var patientLogs = await _db.AuditLogs
                .Where(a => a.UserId == patientId)
                .ToListAsync(ct);

            if (patientLogs.Count > 0)
            {
                foreach (var log in patientLogs)
                {
                    log.UserId    = null;
                    log.IpAddress = "[ANONYMIZED]";
                    log.UserAgent = "[ANONYMIZED]";
                }
                await _db.SaveChangesAsync(ct);
            }

            _logger.LogInformation(
                "DELETION_PHASE6_COMPLETE: PatientId={PatientId}, AuditLogsAnonymized={Count}",
                patientId, patientLogs.Count);

            return true;
        }
        catch (Exception ex)
        {
            var msg = $"Audit log anonymization failed: {ex.Message}";
            _logger.LogWarning(ex,
                "DELETION_AUDIT_ANONYMIZE_FAILED: PatientId={PatientId}. {Message}",
                patientId, msg);
            warnings.Add(msg);
            return false;
        }
    }
}
