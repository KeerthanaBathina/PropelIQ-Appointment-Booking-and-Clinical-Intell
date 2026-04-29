using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.Service.Documents;

namespace UPACIP.Service.PatientRights.Deletion;

/// <summary>
/// Deletes all clinical document files from disk and any pending export ZIPs for the patient
/// (US_094, AC-3, phase 5).
///
/// Path traversal prevention (OWASP A03): every resolved path is validated against the
/// configured storage roots before deletion. Files outside the root are skipped and logged.
///
/// The operation is fail-open: a locked or inaccessible file logs a warning and continues
/// so that the remainder of the patient's files are still deleted.
/// </summary>
public sealed class FileCleanupService
{
    private readonly ApplicationDbContext          _db;
    private readonly string                        _storageRoot;
    private readonly string                        _exportRoot;
    private readonly ILogger<FileCleanupService>   _logger;

    // Export files are written under this root by PatientDataExportService.
    private const string DefaultExportRoot = @"D:\Exports\PatientData";

    public FileCleanupService(
        ApplicationDbContext         db,
        IOptions<DocumentStorageSettings> storageSettings,
        ILogger<FileCleanupService>  logger)
    {
        _db          = db;
        _storageRoot = Path.GetFullPath(storageSettings.Value.StoragePath);
        _exportRoot  = Path.GetFullPath(DefaultExportRoot);
        _logger      = logger;
    }

    /// <summary>
    /// Deletes all clinical document files and export ZIPs for the patient.
    /// Returns the number of files deleted.
    /// </summary>
    public async Task<int> DeletePatientFilesAsync(Guid patientId, CancellationToken ct)
    {
        var deleted = 0;

        // Phase 5a — clinical documents stored by the document storage pipeline.
        var filePaths = await _db.ClinicalDocuments
            .Where(d => d.PatientId == patientId)
            .Select(d => d.FilePath)
            .ToListAsync(ct);

        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                continue;

            if (TryDeleteFile(filePath, _storageRoot, "clinical document"))
                deleted++;
        }

        // Phase 5b — export ZIPs created by PatientDataExportService.
        var exportPaths = await _db.DataAccessRequests
            .Where(r => r.PatientId == patientId && r.ExportFilePath != null)
            .Select(r => r.ExportFilePath!)
            .ToListAsync(ct);

        foreach (var exportPath in exportPaths)
        {
            if (string.IsNullOrWhiteSpace(exportPath))
                continue;

            if (TryDeleteFile(exportPath, _exportRoot, "export ZIP"))
                deleted++;
        }

        _logger.LogInformation(
            "DELETION_FILES_DELETED: PatientId={PatientId}, FilesDeleted={Count}",
            patientId, deleted);

        return deleted;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private bool TryDeleteFile(string rawPath, string allowedRoot, string label)
    {
        try
        {
            // Canonicalize to prevent path traversal (OWASP A03).
            var canonical = Path.GetFullPath(rawPath);
            if (!canonical.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "DELETION_FILE_TRAVERSAL_BLOCKED: Path={Path} is outside allowed root {Root}.",
                    canonical, allowedRoot);
                return false;
            }

            if (!File.Exists(canonical))
                return false;

            File.Delete(canonical);
            _logger.LogInformation(
                "DELETION_FILE_DELETED: Label={Label}, Path={Path}", label, canonical);
            return true;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "DELETION_FILE_SKIPPED: Could not delete {Label} at {Path}.", label, rawPath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "DELETION_FILE_SKIPPED: Access denied deleting {Label} at {Path}.", label, rawPath);
            return false;
        }
    }
}
