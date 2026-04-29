using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UPACIP.DataAccess;
using UPACIP.Service.Documents;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights.Deletion;

/// <summary>
/// Post-deletion verification scan: confirms that no active patient data remains in the
/// primary database, Redis cache, or file system (US_094, AC-4).
///
/// The scan checks six entity tables and two out-of-band stores (Redis, disk).
/// Audit log entries are expected to remain (DR-016, 7-year retention) and are reported
/// as a positive "AuditLogsRetained" flag rather than a failure.
/// </summary>
public sealed class DeletionVerificationService
{
    private readonly ApplicationDbContext                _db;
    private readonly IConnectionMultiplexer              _redis;
    private readonly string                              _storageRoot;
    private readonly string                              _exportRoot;
    private readonly ILogger<DeletionVerificationService> _logger;

    private const string DefaultExportRoot = @"D:\Exports\PatientData";

    public DeletionVerificationService(
        ApplicationDbContext                  db,
        IConnectionMultiplexer                redis,
        IOptions<DocumentStorageSettings>     storageSettings,
        ILogger<DeletionVerificationService>  logger)
    {
        _db          = db;
        _redis       = redis;
        _storageRoot = Path.GetFullPath(storageSettings.Value.StoragePath);
        _exportRoot  = Path.GetFullPath(DefaultExportRoot);
        _logger      = logger;
    }

    /// <summary>
    /// Runs a comprehensive scan and returns a <see cref="DeletionVerificationResult"/>
    /// detailing whether the patient's data has been fully removed.
    /// </summary>
    public async Task<DeletionVerificationResult> VerifyDeletionAsync(Guid patientId, CancellationToken ct)
    {
        var remaining = new List<string>();
        var tablesChecked = 0;

        // ── 1. Active Patients ───────────────────────────────────────────────────
        tablesChecked++;
        var activePatient = await _db.Patients
            .AnyAsync(p => p.Id == patientId && p.DeletedAt == null, ct);
        if (activePatient)
            remaining.Add("Patients (active)");

        // ── 2. Appointments ──────────────────────────────────────────────────────
        tablesChecked++;
        var appointmentCount = await _db.Appointments
            .CountAsync(a => a.PatientId == patientId, ct);
        if (appointmentCount > 0)
            remaining.Add($"Appointments ({appointmentCount} rows)");

        // ── 3. IntakeRecords ─────────────────────────────────────────────────────
        tablesChecked++;
        var intakeCount = await _db.IntakeRecords
            .CountAsync(i => i.PatientId == patientId, ct);
        if (intakeCount > 0)
            remaining.Add($"IntakeRecords ({intakeCount} rows)");

        // ── 4. ClinicalDocuments ─────────────────────────────────────────────────
        tablesChecked++;
        var docCount = await _db.ClinicalDocuments
            .CountAsync(d => d.PatientId == patientId, ct);
        if (docCount > 0)
            remaining.Add($"ClinicalDocuments ({docCount} rows)");

        // ── 5. MedicalCodes (non-sentinel) ───────────────────────────────────────
        tablesChecked++;
        var codeCount = await _db.MedicalCodes
            .CountAsync(m => m.PatientId == patientId && m.PatientId != Guid.Empty, ct);
        if (codeCount > 0)
            remaining.Add($"MedicalCodes (non-sentinel, {codeCount} rows)");

        // ── 6. DataAccessRequests with file paths ────────────────────────────────
        tablesChecked++;
        var exportFileCount = await _db.DataAccessRequests
            .CountAsync(r => r.PatientId == patientId && r.ExportFilePath != null, ct);
        if (exportFileCount > 0)
        {
            // Existence of the request row is expected; ExportFilePath being populated
            // means the file was not removed from disk.
            remaining.Add($"DataAccessRequests with ExportFilePath ({exportFileCount} rows)");
        }

        // ── 7. Audit logs present (expected, per DR-016) ─────────────────────────
        // AuditLog uses ResourceId (the affected entity PK) — patient logs are recorded
        // with ResourceType="Patient" and ResourceId=patientId.
        var auditLogsRetained = await _db.AuditLogs
            .AnyAsync(a => a.ResourceId == patientId, ct);

        // ── 8. Redis cache ───────────────────────────────────────────────────────
        var cacheKeysRemaining = 0;
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints()[0]);
            foreach (var prefix in new[] { "patient", "appointments", "intake" })
            {
                var keys = server.Keys(pattern: $"{prefix}:{patientId}:*").ToArray();
                cacheKeysRemaining += keys.Length;
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex,
                "DELETION_VERIFY_CACHE_UNAVAILABLE: PatientId={PatientId}. Skipping cache check.",
                patientId);
        }

        if (cacheKeysRemaining > 0)
            remaining.Add($"Redis cache keys ({cacheKeysRemaining})");

        // ── 9. Files on disk ─────────────────────────────────────────────────────
        var filesRemaining = CountRemainingFiles(patientId);
        if (filesRemaining > 0)
            remaining.Add($"Files on disk ({filesRemaining})");

        // ── Summary ──────────────────────────────────────────────────────────────
        var result = new DeletionVerificationResult
        {
            FullyDeleted            = remaining.Count == 0,
            RemainingDataLocations  = remaining,
            AuditLogsRetained       = auditLogsRetained,
            TablesVerified          = tablesChecked,
            CacheKeysRemaining      = cacheKeysRemaining,
            FilesRemaining          = filesRemaining,
        };

        if (!result.FullyDeleted)
        {
            _logger.LogWarning(
                "DELETION_VERIFY_INCOMPLETE: PatientId={PatientId}, " +
                "RemainingLocations={Locations}",
                patientId,
                string.Join("; ", remaining));
        }
        else
        {
            _logger.LogInformation(
                "DELETION_VERIFY_COMPLETE: PatientId={PatientId} fully deleted.", patientId);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private int CountRemainingFiles(Guid patientId)
    {
        var count = 0;
        foreach (var root in new[] { _storageRoot, _exportRoot })
        {
            var dir = Path.Combine(root, patientId.ToString());
            if (!Directory.Exists(dir))
                continue;

            try
            {
                count += Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex,
                    "DELETION_VERIFY_FILE_SCAN_ERROR: Could not scan directory {Dir}.", dir);
            }
        }

        return count;
    }
}
