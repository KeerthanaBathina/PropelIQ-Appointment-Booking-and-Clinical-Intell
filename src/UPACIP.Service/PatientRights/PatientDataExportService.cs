using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.PatientRights.DataCollectors;
using UPACIP.Service.PatientRights.Export;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights;

/// <summary>
/// HIPAA Right of Access patient data export service (US_094, NFR-044, AC-1, AC-2).
///
/// Responsibilities:
///   - Submit data access requests with a 30-day SLA deadline (AC-1).
///   - Process requests by collecting all data categories, generating JSON + PDF exports,
///     packaging them in a ZIP archive, and persisting the file path (AC-2).
///   - Authorize and stream completed exports to the requesting patient (OWASP A01).
///   - Record audit log entries for HIPAA compliance evidence.
/// </summary>
public sealed class PatientDataExportService : IPatientDataExportService
{
    private const string ExportBaseDirectory = @"D:\Exports\PatientData";
    private const int    SlaDeadlineDays     = 30;

    private readonly ApplicationDbContext          _db;
    private readonly PatientProfileCollector       _profileCollector;
    private readonly AppointmentCollector          _appointmentCollector;
    private readonly ClinicalDataCollector         _clinicalCollector;
    private readonly JsonExportGenerator           _jsonGenerator;
    private readonly PdfExportGenerator            _pdfGenerator;
    private readonly IAuditLogService              _auditLog;
    private readonly ILogger<PatientDataExportService> _logger;

    public PatientDataExportService(
        ApplicationDbContext               db,
        PatientProfileCollector            profileCollector,
        AppointmentCollector               appointmentCollector,
        ClinicalDataCollector              clinicalCollector,
        JsonExportGenerator                jsonGenerator,
        PdfExportGenerator                 pdfGenerator,
        IAuditLogService                   auditLog,
        ILogger<PatientDataExportService>  logger)
    {
        _db                   = db;
        _profileCollector     = profileCollector;
        _appointmentCollector = appointmentCollector;
        _clinicalCollector    = clinicalCollector;
        _jsonGenerator        = jsonGenerator;
        _pdfGenerator         = pdfGenerator;
        _auditLog             = auditLog;
        _logger               = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Submit
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DataAccessRequest> SubmitRequestAsync(
        Guid patientId, string requestedBy, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var request = new DataAccessRequest
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            RequestType     = "DataAccess",
            Status          = "Submitted",
            RequestedAtUtc  = now,
            DeadlineUtc     = now.AddDays(SlaDeadlineDays),
            RequestedBy     = requestedBy,
            CreatedAt       = now,
            UpdatedAt       = now,
        };

        _db.DataAccessRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DATA_ACCESS_REQUEST_SUBMITTED: PatientId={PatientId}, RequestId={RequestId}, Deadline={Deadline}",
            patientId, request.Id, request.DeadlineUtc);

        // Audit trail for HIPAA compliance (fail-open — log failure must not abort the request).
        await _auditLog.LogAsync(
            action:       AuditAction.DataAccess,
            userId:       null,
            resourceType: "DataAccessRequest",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            resourceId:   request.Id,
            cancellationToken: ct,
            systemEvent:  true);

        return request;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Process
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DataAccessRequest> ProcessRequestAsync(
        Guid requestId, string processedBy, CancellationToken ct)
    {
        var request = await _db.DataAccessRequests
            .Where(r => r.Id == requestId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"DataAccessRequest {requestId} not found.");

        // Idempotency guard — already processed.
        if (request.Status is "Completed" or "Processing")
        {
            _logger.LogWarning(
                "ProcessRequestAsync: request {RequestId} is already in status {Status}.",
                requestId, request.Status);
            return request;
        }

        request.Status    = "Processing";
        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            // Collect all data categories.
            var profile      = await _profileCollector.CollectAsync(request.PatientId, ct);
            var appointments = await _appointmentCollector.CollectAsync(request.PatientId, ct);
            var clinical     = await _clinicalCollector.CollectAsync(request.PatientId, ct);

            var package = new PatientDataPackage
            {
                PatientId         = request.PatientId,
                Profile           = profile,
                Appointments      = appointments,
                IntakeRecords     = clinical.IntakeRecords,
                ClinicalDocuments = clinical.ClinicalDocuments,
                MedicalCodes      = clinical.MedicalCodes,
                ExportedAtUtc     = DateTime.UtcNow,
                ExportVersion     = "1.0",
            };

            // Generate JSON and PDF exports in parallel.
            var jsonTask = _jsonGenerator.GenerateAsync(package, ct);
            var pdfTask  = _pdfGenerator.GenerateAsync(package, ct);
            await Task.WhenAll(jsonTask, pdfTask);

            var jsonBytes = await jsonTask;
            var pdfBytes  = await pdfTask;

            // Package into a ZIP archive.
            var exportDir  = Path.Combine(ExportBaseDirectory, requestId.ToString());
            Directory.CreateDirectory(exportDir);

            var zipPath = Path.Combine(exportDir, $"patient_data_{request.PatientId}.zip");
            await WriteZipAsync(zipPath, request.PatientId, jsonBytes, pdfBytes, ct);

            var fileSize = new FileInfo(zipPath).Length;

            // Finalize the request.
            request.Status             = "Completed";
            request.CompletedAtUtc     = DateTime.UtcNow;
            request.ExportFilePath     = zipPath;
            request.ExportFileSizeBytes = fileSize;
            request.ProcessedBy        = processedBy;
            request.UpdatedAt          = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "DATA_ACCESS_REQUEST_COMPLETED: RequestId={RequestId}, PatientId={PatientId}, SizeBytes={Size}",
                requestId, request.PatientId, fileSize);
        }
        catch (Exception ex)
        {
            request.Status        = "Failed";
            request.FailureReason = ex.Message;
            request.UpdatedAt     = DateTime.UtcNow;

            try { await _db.SaveChangesAsync(ct); }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist failure status for request {RequestId}.", requestId);
            }

            _logger.LogError(
                ex,
                "DATA_ACCESS_REQUEST_FAILED: RequestId={RequestId}, PatientId={PatientId}",
                requestId, request.PatientId);

            throw;
        }

        return request;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Download
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Stream> DownloadExportAsync(
        Guid requestId, Guid patientId, CancellationToken ct)
    {
        var request = await _db.DataAccessRequests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"DataAccessRequest {requestId} not found.");

        // OWASP A01 — Broken Access Control: verify the request belongs to the patient.
        if (request.PatientId != patientId)
        {
            _logger.LogWarning(
                "DownloadExportAsync: patient {PatientId} attempted to download request {RequestId} owned by {Owner}.",
                patientId, requestId, request.PatientId);
            throw new UnauthorizedAccessException("Access to this export is not permitted.");
        }

        if (request.Status != "Completed" || string.IsNullOrEmpty(request.ExportFilePath))
            throw new InvalidOperationException("Export is not yet available.");

        if (!File.Exists(request.ExportFilePath))
            throw new FileNotFoundException("Export file not found.", request.ExportFilePath);

        // Record audit log entry for download event.
        await _auditLog.LogAsync(
            action:       AuditAction.DataAccess,
            userId:       null,
            resourceType: "PatientDataExport",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            resourceId:   requestId,
            cancellationToken: ct,
            systemEvent:  true);

        _logger.LogInformation(
            "DATA_ACCESS_EXPORT_DOWNLOADED: RequestId={RequestId}, PatientId={PatientId}",
            requestId, patientId);

        // FileStream with DeleteOnClose is intentionally not used — the file must be kept
        // for subsequent downloads within the SLA window.
        return new FileStream(request.ExportFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Queries
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<DataAccessRequest>> GetRequestsAsync(
        string? status, bool overdueOnly, CancellationToken ct)
    {
        var query = _db.DataAccessRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);

        if (overdueOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(r => r.Status != "Completed" && r.Status != "Failed" && r.DeadlineUtc < now);
        }

        return await query.OrderBy(r => r.DeadlineUtc).ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<DataAccessRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken ct)
        => await _db.DataAccessRequests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .FirstOrDefaultAsync(ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task WriteZipAsync(
        string zipPath,
        Guid   patientId,
        byte[] jsonBytes,
        byte[] pdfBytes,
        CancellationToken ct)
    {
        await using var zipStream = new FileStream(
            zipPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        var jsonEntry = archive.CreateEntry($"patient_data_{patientId}.json", CompressionLevel.Optimal);
        await using (var entryStream = jsonEntry.Open())
            await entryStream.WriteAsync(jsonBytes, ct);

        var pdfEntry = archive.CreateEntry($"patient_data_{patientId}.pdf", CompressionLevel.Optimal);
        await using (var entryStream = pdfEntry.Open())
            await entryStream.WriteAsync(pdfBytes, ct);
    }
}
