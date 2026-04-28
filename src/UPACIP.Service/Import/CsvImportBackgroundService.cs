using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Import.Models;

namespace UPACIP.Service.Import;

/// <summary>
/// Hosted background service that processes queued CSV import jobs for large files
/// (US_092 task_002, edge case 1 — 100K+ row files, AC-2, AC-3).
///
/// Jobs are submitted to the singleton <see cref="ConcurrentDictionary{TKey,TValue}"/> job store
/// by <c>ImportController</c> with <c>Status = "Queued"</c>.
/// The service polls the store on a 500 ms interval, picks up the oldest queued job,
/// processes it via <see cref="ICsvImportEngine"/>, and updates job state in real-time so
/// the progress endpoint can serve live data.
///
/// Temporary files are always deleted after processing, regardless of outcome.
///
/// Security (OWASP A03): file paths are generated internally via <c>Path.GetTempFileName</c>
/// and are never derived from user input. Entity types are validated against the allowlist
/// before a job is accepted by the controller.
/// </summary>
public sealed class CsvImportBackgroundService : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, ImportJob> _jobs;
    private readonly IServiceScopeFactory                  _scopeFactory;
    private readonly ILogger<CsvImportBackgroundService>   _logger;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(500);

    public CsvImportBackgroundService(
        ConcurrentDictionary<Guid, ImportJob>  jobs,
        IServiceScopeFactory                   scopeFactory,
        ILogger<CsvImportBackgroundService>    logger)
    {
        _jobs         = jobs;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService
    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CsvImportBackgroundService: started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var job = _jobs.Values
                .Where(j => j.Status == "Queued")
                .OrderBy(j => j.CreatedAtUtc)
                .FirstOrDefault();

            if (job is not null)
                await ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
            else
                await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("CsvImportBackgroundService: stopping.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Job processing
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ProcessJobAsync(ImportJob job, CancellationToken ct)
    {
        job.Status = "InProgress";

        _logger.LogInformation(
            "CsvImportBackgroundService: processing job {JobId}, EntityType={EntityType}, File={File}.",
            job.JobId, job.EntityType, job.FileName);

        ImportResult? result = null;

        try
        {
            await using var fileStream = File.OpenRead(job.FilePath);

            // Use a scoped DI scope — ICsvImportEngine is scoped (depends on DbContext).
            await using var scope  = _scopeFactory.CreateAsyncScope();
            var engine             = scope.ServiceProvider.GetRequiredService<ICsvImportEngine>();

            result = job.EntityType switch
            {
                "Patient"     => await engine.ImportAsync<DataAccess.Entities.Patient>(
                    fileStream, progress => job.Progress = progress, ct),
                "Appointment" => await engine.ImportAsync<DataAccess.Entities.Appointment>(
                    fileStream, progress => job.Progress = progress, ct),
                "User"        => await engine.ImportAsync<DataAccess.Entities.ApplicationUser>(
                    fileStream, progress => job.Progress = progress, ct),
                _ => throw new InvalidOperationException($"Unsupported entity type: {job.EntityType}"),
            };

            job.Result = result;
            job.Status = result.Status == ImportStatus.Completed ? "Completed" : "CompletedWithErrors";

            _logger.LogInformation(
                "CSV_IMPORT_COMPLETE: JobId={JobId}, Entity={Entity}, Success={Success}, " +
                "Errors={Errors}, Duplicates={Dups}, Duration={Duration}",
                job.JobId, job.EntityType, result.SuccessCount,
                result.ErrorCount, result.DuplicateCount, result.Duration);

            // Persist audit log
            await PersistImportLogAsync(job, result, scope.ServiceProvider, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Result = new ImportResult
            {
                Status     = ImportStatus.Aborted,
                EntityType = job.EntityType,
                Errors     = [new RowError { RowNumber = 0, ColumnName = "(engine)",
                    ErrorMessage = ex.Message }],
                Duration   = TimeSpan.Zero,
            };

            _logger.LogError(ex,
                "CsvImportBackgroundService: job {JobId} failed with exception.", job.JobId);
        }
        finally
        {
            DeleteTempFile(job.FilePath, job.JobId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Audit log persistence
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PersistImportLogAsync(
        ImportJob        job,
        ImportResult     result,
        IServiceProvider services,
        CancellationToken ct)
    {
        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();

            string?  errorReportJson     = null;
            string?  fullErrorReportPath = null;

            if (result.Errors.Count > 0)
            {
                var inlineErrors = result.Errors.Take(ImportLog.MaxInlineErrors).ToList();
                errorReportJson  = JsonSerializer.Serialize(inlineErrors);

                if (result.Errors.Count > ImportLog.MaxInlineErrors)
                    fullErrorReportPath = await WriteFullErrorReportAsync(
                        job.JobId, result.Errors, ct).ConfigureAwait(false);
            }

            var log = new ImportLog
            {
                EntityType          = result.EntityType,
                FileName            = job.FileName,
                FileSizeBytes       = job.FileSizeBytes,
                TotalRows           = result.TotalRows,
                SuccessCount        = result.SuccessCount,
                ErrorCount          = result.ErrorCount,
                DuplicateCount      = result.DuplicateCount,
                Status              = job.Status,
                ErrorReportJson     = errorReportJson,
                FullErrorReportPath = fullErrorReportPath,
                PerformedBy         = job.PerformedBy,
                DurationSeconds     = result.Duration.TotalSeconds,
                CreatedAtUtc        = job.CreatedAtUtc,
                CompletedAtUtc      = DateTime.UtcNow,
            };

            context.Set<ImportLog>().Add(log);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CsvImportBackgroundService: failed to persist ImportLog for job {JobId} — non-critical.",
                job.JobId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Full error report file
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> WriteFullErrorReportAsync(
        Guid              jobId,
        List<RowError>    errors,
        CancellationToken ct)
    {
        try
        {
            var dir  = Path.Combine(Path.GetTempPath(), "ImportErrors");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{jobId}_errors.json");

            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 65536, useAsync: true);
            await JsonSerializer.SerializeAsync(fs, errors, cancellationToken: ct)
                .ConfigureAwait(false);

            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CsvImportBackgroundService: could not write full error report for job {JobId}.", jobId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Temp file cleanup
    // ─────────────────────────────────────────────────────────────────────────

    private void DeleteTempFile(string filePath, Guid jobId)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CsvImportBackgroundService: could not delete temp file {Path} for job {JobId}.",
                filePath, jobId);
        }
    }
}
