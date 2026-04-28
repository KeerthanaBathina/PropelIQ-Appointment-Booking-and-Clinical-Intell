using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Patients.Models;

namespace UPACIP.Service.Patients;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates patient soft-delete, restore, and admin include-deleted queries
/// (US_087 AC-1, AC-2, AC-3, DR-021, NFR-033).
///
/// Contract summary:
/// <list type="bullet">
///   <item>
///     <see cref="SoftDeleteAsync"/> — sets <c>DeletedAt</c> to the current UTC timestamp
///     instead of issuing a physical DELETE (AC-1).  Blocked when the patient has active
///     scheduled appointments, intake records in processing, or clinical documents queued for
///     AI processing (edge case 1).
///   </item>
///   <item>
///     <see cref="RestoreAsync"/> — clears <c>DeletedAt</c> to null, restoring the patient
///     and all dependent FK-linked data to active status (edge case 2).
///   </item>
///   <item>
///     <see cref="GetPatientsIncludingDeletedAsync"/> — returns a paginated patient list that
///     bypasses the global query filter, annotating each record with <c>IsDeleted</c> and
///     <c>DeletedAt</c> for visual indication (AC-3).
///   </item>
/// </list>
///
/// AC-2 (soft-deleted records excluded from standard queries) is already provided by the
/// <c>HasQueryFilter(p => p.DeletedAt == null)</c> in <c>PatientConfiguration</c> — this service
/// does not need to enforce it; EF Core does it automatically on every non-IgnoreQueryFilters call.
/// </summary>
public interface IPatientSoftDeleteService
{
    /// <summary>
    /// Soft-deletes the patient by setting <c>DeletedAt</c> to the current UTC time (AC-1).
    /// Returns a blocked result when active dependencies exist (edge case 1).
    /// </summary>
    Task<SoftDeleteResult?> SoftDeleteAsync(
        Guid              patientId,
        Guid              performedByUserId,
        string            performedByIp,
        string            performedByUserAgent,
        CancellationToken ct = default);

    /// <summary>
    /// Restores a soft-deleted patient by clearing <c>DeletedAt</c> to <c>null</c> (edge case 2).
    /// Returns <c>false</c> when the patient is not found or is not currently soft-deleted.
    /// </summary>
    Task<bool?> RestoreAsync(
        Guid              patientId,
        Guid              performedByUserId,
        string            performedByIp,
        string            performedByUserAgent,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated patient list that includes soft-deleted records (AC-3).
    /// Each record is annotated with <see cref="PatientListItem.IsDeleted"/> and
    /// <see cref="PatientListItem.DeletedAt"/>.
    /// </summary>
    Task<(IReadOnlyList<PatientListItem> Items, int TotalCount)> GetPatientsIncludingDeletedAsync(
        int               page,
        int               pageSize,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scoped implementation of <see cref="IPatientSoftDeleteService"/>.
/// Writes immutable <see cref="AuditLog"/> entries for every soft-delete and restore
/// operation to maintain a HIPAA-compliant audit trail (US_064).
/// </summary>
public sealed class PatientSoftDeleteService : IPatientSoftDeleteService
{
    private readonly ApplicationDbContext              _db;
    private readonly ILogger<PatientSoftDeleteService> _logger;

    public PatientSoftDeleteService(
        ApplicationDbContext              db,
        ILogger<PatientSoftDeleteService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SoftDeleteAsync (AC-1, edge case 1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SoftDeleteResult?> SoftDeleteAsync(
        Guid              patientId,
        Guid              performedByUserId,
        string            performedByIp,
        string            performedByUserAgent,
        CancellationToken ct = default)
    {
        // IgnoreQueryFilters so we can detect and reject re-deletion attempts on already-soft-deleted patients.
        var patient = await _db.Patients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == patientId, ct);

        if (patient is null)
            return null; // caller maps to 404

        // Idempotency guard — do not overwrite an existing DeletedAt timestamp.
        if (patient.DeletedAt is not null)
            return SoftDeleteResult.Blocked("Patient is already soft-deleted.");

        // ── Dependency check (edge case 1) ────────────────────────────────────
        var blocking = new List<ActiveDependency>();

        var scheduledAppointments = await _db.Appointments
            .CountAsync(a => a.PatientId == patientId && a.Status == AppointmentStatus.Scheduled, ct);

        if (scheduledAppointments > 0)
            blocking.Add(new ActiveDependency
            {
                EntityType = "Appointment",
                Count      = scheduledAppointments,
                Detail     = $"{scheduledAppointments} scheduled appointment{(scheduledAppointments == 1 ? "" : "s")}",
            });

        var activeIntake = await _db.IntakeRecords
            .CountAsync(i => i.PatientId == patientId
                          && i.AiSessionStatus == "active", ct);

        if (activeIntake > 0)
            blocking.Add(new ActiveDependency
            {
                EntityType = "IntakeData",
                Count      = activeIntake,
                Detail     = $"{activeIntake} intake record{(activeIntake == 1 ? "" : "s")} currently in processing",
            });

        var queuedDocuments = await _db.ClinicalDocuments
            .CountAsync(d => d.PatientId == patientId
                          && (d.ProcessingStatus == ProcessingStatus.Queued
                           || d.ProcessingStatus == ProcessingStatus.Processing), ct);

        if (queuedDocuments > 0)
            blocking.Add(new ActiveDependency
            {
                EntityType = "ClinicalDocument",
                Count      = queuedDocuments,
                Detail     = $"{queuedDocuments} clinical document{(queuedDocuments == 1 ? "" : "s")} queued for AI processing",
            });

        if (blocking.Count > 0)
            return SoftDeleteResult.Blocked(
                "Patient has active dependencies that must be resolved before deletion.",
                blocking);

        // ── Apply soft delete ──────────────────────────────────────────────────
        patient.DeletedAt = DateTime.UtcNow;

        AppendAuditLog(
            userId:    performedByUserId,
            action:    AuditAction.DataDelete,
            resource:  "Patient",
            resourceId: patientId,
            ip:        performedByIp,
            userAgent: performedByUserAgent);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PATIENT_SOFT_DELETED: PatientId={PatientId}, PerformedBy={UserId}, DeletedAt={DeletedAt:o}",
            patientId, performedByUserId, patient.DeletedAt);

        return SoftDeleteResult.Succeeded();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RestoreAsync (edge case 2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool?> RestoreAsync(
        Guid              patientId,
        Guid              performedByUserId,
        string            performedByIp,
        string            performedByUserAgent,
        CancellationToken ct = default)
    {
        // IgnoreQueryFilters — a soft-deleted patient is hidden from normal queries.
        var patient = await _db.Patients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == patientId, ct);

        if (patient is null)
            return null; // caller maps to 404

        if (patient.DeletedAt is null)
            return false; // not soft-deleted — caller maps to 409 or 404

        // ── Clear soft-delete sentinel ────────────────────────────────────────
        patient.DeletedAt = null;

        // Dependent data (Appointments, IntakeRecords, ClinicalDocuments, MedicalCodes)
        // was never physically removed — all FK-linked rows remain intact. Clearing
        // DeletedAt is sufficient to restore full visibility because every join on
        // Patient goes through EF Core's global query filter on the Patient table.

        AppendAuditLog(
            userId:    performedByUserId,
            action:    AuditAction.DataModify,
            resource:  "Patient",
            resourceId: patientId,
            ip:        performedByIp,
            userAgent: performedByUserAgent);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PATIENT_RESTORED: PatientId={PatientId}, PerformedBy={UserId}",
            patientId, performedByUserId);

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetPatientsIncludingDeletedAsync (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<PatientListItem> Items, int TotalCount)>
        GetPatientsIncludingDeletedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        // Clamp to prevent unreasonable page sizes.
        pageSize = Math.Clamp(pageSize, 1, 200);
        page     = Math.Max(page, 1);

        var query = _db.Patients
            .IgnoreQueryFilters()
            .AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.FullName)
            .ThenBy(p => p.Id)          // stable secondary sort
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PatientListItem
            {
                PatientId  = p.Id,
                FullName   = p.FullName,
                Email      = p.Email,
                CreatedAt  = p.CreatedAt,
                IsDeleted  = p.DeletedAt != null,
                DeletedAt  = p.DeletedAt,
            })
            .ToListAsync(ct);

        return (items, totalCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends an immutable <see cref="AuditLog"/> entry to the current change-tracker batch.
    /// The entry is persisted in the same <c>SaveChangesAsync</c> call as the patient update,
    /// ensuring transactional consistency between the operation and its audit trail.
    /// </summary>
    private void AppendAuditLog(
        Guid        userId,
        AuditAction action,
        string      resource,
        Guid        resourceId,
        string      ip,
        string      userAgent)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = userId,
            Action       = action,
            ResourceType = resource,
            ResourceId   = resourceId,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = ip,
            UserAgent    = userAgent,
        });
    }
}
