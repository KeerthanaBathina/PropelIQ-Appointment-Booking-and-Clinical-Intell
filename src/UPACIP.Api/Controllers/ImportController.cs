using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Import;
using UPACIP.Service.Import.Models;
using UPACIP.Service.Import.Profiles;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for bulk CSV import of Patient, Appointment, and User data
/// (US_092 task_002, AC-2, AC-3, edge case 1).
///
/// Routes:
///   POST /api/admin/import/{entityType}          — Upload and import CSV (sync or async).
///   GET  /api/admin/import/jobs/{jobId}          — Poll background job progress.
///   POST /api/admin/import/{entityType}/preview  — Validate headers + first 10 rows (dry run).
///   GET  /api/admin/import/history               — Paginated import audit log.
///
/// Synchronous path (≤10,000 estimated rows): executes inline and returns <see cref="ImportResult"/>.
/// Asynchronous path (&gt;10,000 estimated rows): returns <c>202 Accepted</c> with a job ID.
///
/// Security (OWASP A01, A03):
///   All endpoints require the Admin role.
///   File uploads are validated for extension (.csv only), content type, and size.
///   Temporary files are stored in <see cref="Path.GetTempPath"/> and deleted after processing.
///   No user-controlled strings reach SQL — all DB access goes through EF Core.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Route("api/admin/import")]
[Produces("application/json")]
public sealed class ImportController : ControllerBase
{
    private const int SyncRowThreshold       = 10_000;
    private const int AvgBytesPerRow         = 200;
    private static readonly string[] AllowedExtensions  = [".csv"];
    private static readonly string[] AllowedContentTypes =
        ["text/csv", "application/octet-stream", "application/vnd.ms-excel", "application/csv"];

    private readonly ICsvImportEngine                      _engine;
    private readonly ICsvParser                            _parser;
    private readonly IOptionsMonitor<ImportOptions>        _options;
    private readonly ConcurrentDictionary<Guid, ImportJob> _jobs;
    private readonly ApplicationDbContext                  _context;
    private readonly IServiceProvider                      _serviceProvider;
    private readonly ILogger<ImportController>             _logger;

    public ImportController(
        ICsvImportEngine                       engine,
        ICsvParser                             parser,
        IOptionsMonitor<ImportOptions>         options,
        ConcurrentDictionary<Guid, ImportJob>  jobs,
        ApplicationDbContext                   context,
        IServiceProvider                       serviceProvider,
        ILogger<ImportController>              logger)
    {
        _engine          = engine;
        _parser          = parser;
        _options         = options;
        _jobs            = jobs;
        _context         = context;
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    // ── POST /api/admin/import/{entityType} ──────────────────────────────────

    /// <summary>
    /// Uploads a CSV file and imports its rows into the specified entity table (AC-2, AC-3).
    /// Files estimated at ≤10,000 rows are processed synchronously and return <see cref="ImportResult"/>.
    /// Files estimated at &gt;10,000 rows are queued for background processing and return 202 Accepted.
    /// </summary>
    [HttpPost("{entityType}")]
    [RequestSizeLimit(55_000_000)] // 50 MB + headroom
    [ProducesResponseType(typeof(ImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Import(
        [FromRoute] string entityType,
        IFormFile          file,
        CancellationToken  ct)
    {
        var opts = _options.CurrentValue;

        // ── Validate entity type ──────────────────────────────────────────
        if (!opts.AllowedEntityTypes.Contains(entityType,
            StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error           = $"Entity type '{entityType}' is not allowed.",
                allowedTypes    = opts.AllowedEntityTypes,
            });
        }

        // ── Validate uploaded file ────────────────────────────────────────
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded or file is empty." });

        var validationError = ValidateUploadedFile(file, opts);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        var performedBy = ResolveAdminIdentity();

        _logger.LogInformation(
            "ImportController: {Admin} initiated {EntityType} import. File={File}, Size={Size}.",
            performedBy, entityType, file.FileName, file.Length);

        // ── Decide sync vs async ──────────────────────────────────────────
        long estimatedRows = file.Length / AvgBytesPerRow;

        if (estimatedRows <= SyncRowThreshold)
        {
            // Synchronous path
            await using var stream = file.OpenReadStream();
            var result             = await DispatchImportAsync(entityType, stream, ct);

            await PersistImportLogAsync(entityType, file, result, performedBy, ct);

            return Ok(result);
        }
        else
        {
            // Asynchronous path — save temp file and queue
            var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".csv");
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 65536, useAsync: true))
            {
                await file.CopyToAsync(fs, ct);
            }

            var job = new ImportJob
            {
                EntityType    = entityType,
                FilePath      = tempPath,
                FileName      = Path.GetFileName(file.FileName),
                FileSizeBytes = file.Length,
                PerformedBy   = performedBy,
            };

            _jobs[job.JobId] = job;

            _logger.LogInformation(
                "ImportController: large file queued. JobId={JobId}, EstimatedRows={Rows}.",
                job.JobId, estimatedRows);

            return Accepted(new
            {
                jobId     = job.JobId,
                statusUrl = $"/api/admin/import/jobs/{job.JobId}",
            });
        }
    }

    // ── GET /api/admin/import/jobs/{jobId} ───────────────────────────────────

    /// <summary>
    /// Returns the current status and progress of a background import job.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJobStatus([FromRoute] Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return NotFound(new { error = $"Import job '{jobId}' not found." });

        return Ok(new
        {
            job.JobId,
            job.EntityType,
            job.FileName,
            job.Status,
            Progress = job.Progress,
            Result   = job.Result,
        });
    }

    // ── POST /api/admin/import/{entityType}/preview ──────────────────────────

    /// <summary>
    /// Validates CSV headers and the first 10 data rows without persisting any data.
    /// Returns detected delimiter, header validity, and sample row validation errors (dry run).
    /// </summary>
    [HttpPost("{entityType}/preview")]
    [RequestSizeLimit(55_000_000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(
        [FromRoute] string entityType,
        IFormFile          file,
        CancellationToken  ct)
    {
        var opts = _options.CurrentValue;

        if (!opts.AllowedEntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Entity type '{entityType}' is not allowed." });

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded or file is empty." });

        var validationError = ValidateUploadedFile(file, opts);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        await using var stream = file.OpenReadStream();

        // Read headers
        var headers = await _parser.ReadHeadersAsync(stream, ct);

        // Detect delimiter by re-reading first line
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        string? detectedDelimiter = null;
        var firstLine = string.Empty;
        using (var sr = new System.IO.StreamReader(stream, leaveOpen: true))
            firstLine = await sr.ReadLineAsync(ct) ?? string.Empty;
        if (firstLine.Contains('\t'))  detectedDelimiter = "TAB";
        else if (firstLine.Contains(';')) detectedDelimiter = ";";
        else                              detectedDelimiter = ",";

        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);

        // Validate required columns
        var (requiredCols, validateRow) = ResolvePreviewHandlers(entityType);
        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        var missing   = requiredCols.Where(c => !headerSet.Contains(c)).ToList();

        // Sample first 10 data rows
        var sampleErrors = new List<RowError>();
        int sampleCount  = 0;

        if (validateRow is not null && missing.Count == 0)
        {
            await foreach (var (lineNumber, fields) in _parser.ParseAsync(stream, ct))
            {
                if (sampleCount >= 10)
                    break;
                sampleErrors.AddRange(validateRow(fields, lineNumber));
                sampleCount++;
            }
        }

        long estimatedRows = file.Length / AvgBytesPerRow;

        return Ok(new
        {
            headersValid       = missing.Count == 0,
            missingColumns     = missing,
            detectedDelimiter,
            estimatedRowCount  = estimatedRows,
            sampleRowsChecked  = sampleCount,
            sampleErrors,
        });
    }

    // ── GET /api/admin/import/history ────────────────────────────────────────

    /// <summary>
    /// Returns a paginated, descending-ordered list of past import operations.
    /// Supports filtering by <paramref name="entityType"/> and <paramref name="status"/>.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? entityType,
        [FromQuery] string? status,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        CancellationToken   ct       = default)
    {
        if (page < 1)     page     = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Set<ImportLog>().AsNoTracking();

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(l => l.EntityType == entityType);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(l => l.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.EntityType,
                l.FileName,
                l.FileSizeBytes,
                l.TotalRows,
                l.SuccessCount,
                l.ErrorCount,
                l.DuplicateCount,
                l.Status,
                l.PerformedBy,
                l.DurationSeconds,
                l.CreatedAtUtc,
                l.CompletedAtUtc,
                HasFullErrorReport = l.FullErrorReportPath != null,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            Items    = items,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ImportResult> DispatchImportAsync(
        string entityType, Stream stream, CancellationToken ct)
    {
        return entityType.ToLowerInvariant() switch
        {
            "patient"     => await _engine.ImportAsync<Patient>(stream, ct: ct),
            "appointment" => await _engine.ImportAsync<Appointment>(stream, ct: ct),
            "user"        => await _engine.ImportAsync<ApplicationUser>(stream, ct: ct),
            _             => new ImportResult
            {
                Status     = ImportStatus.HeaderValidationFailed,
                EntityType = entityType,
                Errors     = [new RowError { RowNumber = 0, ColumnName = "(entity)",
                    ErrorMessage = $"Unsupported entity type: {entityType}" }],
            },
        };
    }

    /// <summary>
    /// Returns the required columns list and a typed validate-row delegate for the
    /// preview endpoint. Uses an explicit switch to preserve generic type information.
    /// </summary>
    private (string[] RequiredCols, Func<Dictionary<string, string>, int, List<RowError>>? ValidateRow)
        ResolvePreviewHandlers(string entityType)
    {
        return entityType.ToLowerInvariant() switch
        {
            "patient" when _serviceProvider.GetService(typeof(ICsvImportProfile<Patient>))
                is ICsvImportProfile<Patient> p
                => (p.RequiredColumns, p.ValidateRow),

            "appointment" when _serviceProvider.GetService(typeof(ICsvImportProfile<Appointment>))
                is ICsvImportProfile<Appointment> a
                => (a.RequiredColumns, a.ValidateRow),

            "user" when _serviceProvider.GetService(typeof(ICsvImportProfile<ApplicationUser>))
                is ICsvImportProfile<ApplicationUser> u
                => (u.RequiredColumns, u.ValidateRow),

            _ => ([], null),
        };
    }

    private object? ResolveProfile(string entityType) =>
        // Kept for future use; use ResolvePreviewHandlers for type-safe access.
        null;

    private async Task PersistImportLogAsync(
        string entityType, IFormFile file, ImportResult result, string performedBy, CancellationToken ct)
    {
        try
        {
            string?  errorReportJson     = null;
            string?  fullErrorReportPath = null;

            if (result.Errors.Count > 0)
            {
                var inline     = result.Errors.Take(ImportLog.MaxInlineErrors).ToList();
                errorReportJson = JsonSerializer.Serialize(inline);

                if (result.Errors.Count > ImportLog.MaxInlineErrors)
                {
                    try
                    {
                        var dir  = Path.Combine(Path.GetTempPath(), "ImportErrors");
                        Directory.CreateDirectory(dir);
                        var jobId = Guid.NewGuid();
                        var path  = Path.Combine(dir, $"{jobId}_errors.json");
                        await System.IO.File.WriteAllTextAsync(path,
                            JsonSerializer.Serialize(result.Errors), ct);
                        fullErrorReportPath = path;
                    }
                    catch { /* non-critical */ }
                }
            }

            var log = new ImportLog
            {
                EntityType          = entityType,
                FileName            = Path.GetFileName(file.FileName),
                FileSizeBytes       = file.Length,
                TotalRows           = result.TotalRows,
                SuccessCount        = result.SuccessCount,
                ErrorCount          = result.ErrorCount,
                DuplicateCount      = result.DuplicateCount,
                Status              = result.Status.ToString(),
                ErrorReportJson     = errorReportJson,
                FullErrorReportPath = fullErrorReportPath,
                PerformedBy         = performedBy,
                DurationSeconds     = result.Duration.TotalSeconds,
                CompletedAtUtc      = DateTime.UtcNow,
            };

            _context.Set<ImportLog>().Add(log);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ImportController: failed to persist ImportLog — non-critical.");
        }
    }

    /// <summary>
    /// Validates extension, content type, and file size.
    /// Returns an error message string on failure, or <c>null</c> when valid.
    /// </summary>
    private static string? ValidateUploadedFile(IFormFile file, ImportOptions opts)
    {
        // Extension check (OWASP A03 — prevent disguised executables)
        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return $"Only .csv files are accepted. Received: '{ext}'.";

        // Content type check
        if (!AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return $"Unsupported content type '{file.ContentType}'.";

        // File size
        if (file.Length > opts.MaxFileSizeBytes)
            return $"File size {file.Length / 1_048_576} MB exceeds maximum {opts.MaxFileSizeBytes / 1_048_576} MB.";

        return null;
    }

    private string ResolveAdminIdentity() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(ClaimTypes.Name)
        ?? "unknown";
}
