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
/// Manages the archival of expired appointment records from the main
/// <c>appointments</c> table to <c>archive.appointments</c> (US_086 AC-3, AC-5).
///
/// Both operations:
/// <list type="bullet">
///   <item>Move the full row to <c>archive.appointments</c> via a raw-SQL INSERT-SELECT.</item>
///   <item>Retain an <see cref="ArchivedAppointmentReference"/> stub in the main schema so that
///         patient-history queries can still surface the appointment on the timeline.</item>
///   <item>Respect audit-log reference protection from <see cref="IRetentionPolicyGuard"/>
///         (edge case 2).</item>
///   <item>Defer appointments that still have active <c>NotificationLog</c> rows
///         (the notification purge runs first in the nightly cycle).</item>
/// </list>
/// </summary>
public interface IAppointmentArchivalService
{
    /// <summary>
    /// Archives completed appointments older than
    /// <see cref="RetentionPolicyOptions.AppointmentRetentionYears"/> (AC-3).
    /// </summary>
    Task<ArchivalResult> ArchiveCompletedAppointmentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Archives cancelled appointments older than
    /// <see cref="RetentionPolicyOptions.CancelledAppointmentRetentionYears"/> (AC-5).
    /// </summary>
    Task<ArchivalResult> ArchiveCancelledAppointmentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches the full appointment record from <c>archive.appointments</c> by the original
    /// appointment ID.  Returns <c>null</c> when the appointment is not in the archive.
    /// Used by patient-history endpoints when a user clicks "view archived details" on a
    /// reference stub.  The result is read-only (change-tracking disabled).
    /// </summary>
    Task<Appointment?> GetArchivedAppointmentAsync(Guid appointmentId, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scoped implementation of <see cref="IAppointmentArchivalService"/>.
///
/// Archival loop (per batch):
/// <list type="number">
///   <item>Load a batch of eligible appointment candidates (minimal projection).</item>
///   <item>Skip if referenced by an audit log within the 7-year window (edge case 2).</item>
///   <item>Skip if active <c>NotificationLog</c> rows still reference the appointment via FK.</item>
///   <item>In a single database transaction per appointment:
///     <list type="bullet">
///       <item>INSERT-SELECT the full row into <c>archive.appointments</c> (ON CONFLICT DO NOTHING for idempotency).</item>
///       <item>Add an <see cref="ArchivedAppointmentReference"/> stub and <c>SaveChangesAsync</c>.</item>
///       <item>DELETE the main-table row via <c>ExecuteDeleteAsync</c> (EF Core 8 bulk delete).</item>
///     </list>
///   </item>
///   <item>Log per-appointment failures and continue to the next candidate — a single-row
///         failure must not abort the entire batch.</item>
/// </list>
///
/// If all candidates in a batch are protected/deferred, iteration stops to prevent an
/// infinite loop over the same rows (mirrors the pattern in <see cref="DataRetentionService"/>).
/// </summary>
public sealed class AppointmentArchivalService : IAppointmentArchivalService
{
    private readonly ApplicationDbContext                    _db;
    private readonly IRetentionPolicyGuard                   _guard;
    private readonly IOptionsMonitor<RetentionPolicyOptions> _options;
    private readonly ILogger<AppointmentArchivalService>     _logger;

    public AppointmentArchivalService(
        ApplicationDbContext                    db,
        IRetentionPolicyGuard                   guard,
        IOptionsMonitor<RetentionPolicyOptions> options,
        ILogger<AppointmentArchivalService>     logger)
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
    public Task<ArchivalResult> ArchiveCompletedAppointmentsAsync(CancellationToken ct = default)
    {
        var opts   = _options.CurrentValue;
        var cutoff = DateTime.UtcNow.AddYears(-opts.AppointmentRetentionYears);
        return ArchiveAsync(AppointmentStatus.Completed, RetentionCategory.Appointments, cutoff, ct);
    }

    /// <inheritdoc/>
    public Task<ArchivalResult> ArchiveCancelledAppointmentsAsync(CancellationToken ct = default)
    {
        var opts   = _options.CurrentValue;
        var cutoff = DateTime.UtcNow.AddYears(-opts.CancelledAppointmentRetentionYears);
        return ArchiveAsync(AppointmentStatus.Cancelled, RetentionCategory.CancelledAppointments, cutoff, ct);
    }

    /// <inheritdoc/>
    public async Task<Appointment?> GetArchivedAppointmentAsync(
        Guid              appointmentId,
        CancellationToken ct = default)
    {
        // Query archive.appointments via raw SQL using the main Appointments DbSet so that
        // EF Core can map all columns (including the JSONB PreferredSlotCriteria owned type).
        // The extra archived_at_utc column in archive.appointments is silently ignored.
        // AsNoTracking() is mandatory: archived rows must never be tracked or modified.
        return await _db.Appointments
            .FromSql($"""
                SELECT "Id", "PatientId", "BookingReference", "AppointmentTime", "Status",
                       "IsWalkIn", "ProviderId", "ProviderName", "AppointmentType",
                       "PreferredSlotCriteria", "Version",
                       no_show_risk_score, no_show_risk_band, is_risk_estimated,
                       requires_outreach, risk_calculated_at_utc,
                       "SlotTemplateId", "CreatedAt", "UpdatedAt"
                FROM   archive.appointments
                WHERE  "Id" = {appointmentId}
                """)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core archival loop
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ArchivalResult> ArchiveAsync(
        AppointmentStatus status,
        RetentionCategory category,
        DateTime          cutoff,
        CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        var sw   = Stopwatch.StartNew();

        int totalArchived     = 0;
        int totalAuditSkipped = 0;
        int totalFkSkipped    = 0;
        DateTime? oldestDate  = null;
        DateTime? newestDate  = null;

        _logger.LogInformation(
            "AppointmentArchivalService: starting {Category} archival. " +
            "Status={Status}, Cutoff={Cutoff:o}, BatchSize={BatchSize}",
            category, status, cutoff, opts.PurgeBatchSize);

        bool hasMore = true;
        while (hasMore && !ct.IsCancellationRequested)
        {
            // ── Load next batch of candidates (minimal projection) ──────────────
            var candidates = await _db.Appointments
                .AsNoTracking()
                .Where(a => a.Status == status && a.CreatedAt < cutoff)
                .OrderBy(a => a.CreatedAt) // oldest-first so partial runs make visible progress
                .Select(a => new { a.Id, a.PatientId, a.AppointmentTime, a.Status, a.CreatedAt })
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

                // ── Audit-log reference protection (edge case 2) ──────────────
                if (opts.EnforceAuditLogReferenceProtection)
                {
                    bool auditProtected = await _guard.IsProtectedByAuditLogAsync(
                        "Appointment", candidate.Id, ct);

                    if (auditProtected)
                    {
                        _logger.LogDebug(
                            "ARCHIVAL_DEFERRED: Appointment {Id} is protected by an audit log entry " +
                            "within the {Years}-year window. Skipping until protection expires.",
                            candidate.Id, opts.AuditLogRetentionYears);
                        totalAuditSkipped++;
                        continue;
                    }
                }

                // ── Active NotificationLog FK check ──────────────────────────
                // Appointments being archived are 3+ years old. Their notification logs
                // should have been purged at the 90-day mark. However if audit-log
                // protection kept a notification log alive, we defer the appointment so
                // that the notification purge step can clear it first.
                var activeNotifCount = await _db.NotificationLogs
                    .Where(n => n.AppointmentId == candidate.Id)
                    .CountAsync(ct);

                if (activeNotifCount > 0)
                {
                    _logger.LogWarning(
                        "ARCHIVAL_DEFERRED: Appointment {Id} has {Count} active notification log " +
                        "row(s) referencing it. Deferring until notification purge clears them.",
                        candidate.Id, activeNotifCount);
                    totalFkSkipped++;
                    continue;
                }

                // ── Archive single appointment within a transaction ───────────
                bool archived = await ArchiveSingleAppointmentAsync(
                    candidate.Id,
                    candidate.PatientId,
                    candidate.AppointmentTime,
                    candidate.Status.ToString(),
                    ct);

                if (archived)
                {
                    batchArchived++;
                    totalArchived++;

                    if (oldestDate is null || candidate.AppointmentTime < oldestDate)
                        oldestDate = candidate.AppointmentTime;
                    if (newestDate is null || candidate.AppointmentTime > newestDate)
                        newestDate = candidate.AppointmentTime;
                }
            }

            _logger.LogDebug(
                "AppointmentArchivalService: batch complete — archived {BatchCount} {Category} appointments.",
                batchArchived, category);

            // Stop iterating if nothing in this batch was archivable, to avoid an infinite
            // loop over the same audit-protected or FK-blocked rows.
            hasMore = candidates.Count == opts.PurgeBatchSize && batchArchived > 0;
        }

        sw.Stop();

        var result = new ArchivalResult
        {
            Category                    = category,
            RecordsArchived             = totalArchived,
            RecordsSkippedAuditProtected = totalAuditSkipped,
            RecordsSkippedActiveFk      = totalFkSkipped,
            OldestArchivedDate          = oldestDate,
            NewestArchivedDate          = newestDate,
            ExecutionDuration           = sw.Elapsed,
        };

        EmitArchivalSummary(result);
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Single-appointment archival (transactional)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> ArchiveSingleAppointmentAsync(
        Guid              appointmentId,
        Guid              patientId,
        DateTime          appointmentTime,
        string            status,
        CancellationToken ct)
    {
        var archivedAt = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // ── Step 1: INSERT-SELECT into archive.appointments (raw SQL) ─────
            // ON CONFLICT DO NOTHING makes the operation idempotent: if this appointment
            // was already archived in a previous partial run, the INSERT is a no-op and
            // we proceed to clean up any leftover main-table row.
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
                FROM appointments
                WHERE "Id" = {appointmentId}
                ON CONFLICT ("Id") DO NOTHING
                """, ct);

            // ── Step 2: Upsert the reference stub ─────────────────────────────
            // Check if a reference already exists (idempotency guard for partial runs).
            bool refExists = await _db.ArchivedAppointmentReferences
                .AnyAsync(r => r.Id == appointmentId, ct);

            if (!refExists)
            {
                _db.ArchivedAppointmentReferences.Add(new ArchivedAppointmentReference
                {
                    Id              = appointmentId,
                    PatientId       = patientId,
                    AppointmentTime = appointmentTime,
                    Status          = status,
                    ArchivedAtUtc   = archivedAt,
                    ArchiveTable    = "archive.appointments",
                });
                await _db.SaveChangesAsync(ct);
            }

            // ── Step 3: Delete from main appointments table ───────────────────
            // ExecuteDeleteAsync participates in the current transaction (EF Core 8).
            // The DELETE cascades to QueueEntries (expected for 3-year-old appointments).
            // NotificationLogs were verified absent before this method was called.
            await _db.Appointments
                .Where(a => a.Id == appointmentId)
                .ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogDebug(
                "AppointmentArchivalService: archived appointment {Id} → archive.appointments. " +
                "ArchivedAt={ArchivedAt:o}",
                appointmentId, archivedAt);

            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            _logger.LogError(ex,
                "AppointmentArchivalService: failed to archive appointment {Id}. " +
                "Transaction rolled back. Appointment will be retried on next cycle.",
                appointmentId);

            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Logging helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void EmitArchivalSummary(ArchivalResult result)
    {
        if (result.RecordsArchived == 0
            && result.RecordsSkippedAuditProtected == 0
            && result.RecordsSkippedActiveFk == 0)
        {
            _logger.LogDebug(
                "RETENTION_ARCHIVAL_COMPLETE: Category={Category}, Archived=0, " +
                "Duration={DurationMs}ms. No records eligible for archival.",
                result.Category, (int)result.ExecutionDuration.TotalMilliseconds);
            return;
        }

        _logger.LogInformation(
            "RETENTION_ARCHIVAL_COMPLETE: Category={Category}, Archived={Count}, " +
            "Skipped(AuditProtected)={AuditSkipped}, Skipped(ActiveFK)={FkSkipped}, " +
            "OldestArchived={OldestDate:o}, NewestArchived={NewestDate:o}, " +
            "Duration={DurationMs}ms",
            result.Category,
            result.RecordsArchived,
            result.RecordsSkippedAuditProtected,
            result.RecordsSkippedActiveFk,
            result.OldestArchivedDate,
            result.NewestArchivedDate,
            (int)result.ExecutionDuration.TotalMilliseconds);
    }
}
