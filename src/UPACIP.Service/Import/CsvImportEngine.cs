using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Import.Models;
using UPACIP.Service.Import.Profiles;

namespace UPACIP.Service.Import;

/// <summary>
/// Contract for the CSV import pipeline (US_092 task_001, AC-1, AC-2, AC-3, AC-4).
/// </summary>
public interface ICsvImportEngine
{
    /// <summary>
    /// Parses, validates, and persists rows from <paramref name="csvStream"/> into the
    /// <typeparamref name="T"/> table.
    /// <list type="bullet">
    ///   <item>AC-1: validates column headers and field values before any insert.</item>
    ///   <item>AC-2: returns a count of successfully imported rows.</item>
    ///   <item>AC-3: produces a per-row, per-field error report for failed rows.</item>
    ///   <item>AC-4: skips rows that would violate unique constraints and logs them.</item>
    /// </list>
    /// The optional <paramref name="onProgress"/> callback is invoked after every batch
    /// flush with live progress data (used by background jobs for progress polling).
    /// </summary>
    Task<ImportResult> ImportAsync<T>(
        Stream                 csvStream,
        Action<ImportProgress>? onProgress = null,
        CancellationToken      ct          = default)
        where T : class;
}

/// <summary>
/// Scoped implementation of <see cref="ICsvImportEngine"/>.
///
/// Batch persistence (AC-2): valid entities are accumulated and flushed to the database
/// every <see cref="ImportOptions.BatchSize"/> rows. Each batch is wrapped in an
/// independent <c>SaveChangesAsync</c> call so a single-batch failure does not abort
/// unrelated rows; failed batches are logged and their rows added to the error report.
///
/// Security (OWASP A03): no user-controlled strings reach SQL — all persistence is via
/// EF Core parameterised queries. File size is validated up-front to prevent memory exhaustion.
/// PII: email and patient_id values are never included in log messages or error RawValues.
/// </summary>
public sealed class CsvImportEngine : ICsvImportEngine
{
    private readonly ICsvParser                          _parser;
    private readonly ApplicationDbContext                _context;
    private readonly IOptionsMonitor<ImportOptions>      _options;
    private readonly IServiceProvider                    _serviceProvider;
    private readonly ILogger<CsvImportEngine>            _logger;

    public CsvImportEngine(
        ICsvParser                      parser,
        ApplicationDbContext            context,
        IOptionsMonitor<ImportOptions>  options,
        IServiceProvider                serviceProvider,
        ILogger<CsvImportEngine>        logger)
    {
        _parser          = parser;
        _context         = context;
        _options         = options;
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICsvImportEngine
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ImportResult> ImportAsync<T>(
        Stream                 csvStream,
        Action<ImportProgress>? onProgress = null,
        CancellationToken      ct          = default)
        where T : class
    {
        var opts = _options.CurrentValue;
        var sw   = Stopwatch.StartNew();

        // Estimate total batches from file size (heuristic: ~200 bytes per row)
        const int AvgBytesPerRow = 200;
        long estimatedRows    = csvStream.CanSeek ? csvStream.Length / AvgBytesPerRow : 0;
        int  estimatedBatches = opts.BatchSize > 0
            ? (int)Math.Max(1, (estimatedRows + opts.BatchSize - 1) / opts.BatchSize)
            : 1;

        // ── Resolve profile ────────────────────────────────────────────────
        var profile = _serviceProvider.GetService(typeof(ICsvImportProfile<T>)) as ICsvImportProfile<T>;
        if (profile is null)
        {
            _logger.LogError("CsvImportEngine: no ICsvImportProfile<{Type}> registered.", typeof(T).Name);
            return new ImportResult
            {
                Status     = ImportStatus.HeaderValidationFailed,
                EntityType = typeof(T).Name,
                Errors     = [new RowError { RowNumber = 0, ColumnName = "(engine)",
                    ErrorMessage = $"No import profile registered for entity type '{typeof(T).Name}'." }],
                Duration   = sw.Elapsed,
            };
        }

        // ── File size guard ───────────────────────────────────────────────
        if (csvStream.CanSeek && csvStream.Length > opts.MaxFileSizeBytes)
        {
            _logger.LogWarning(
                "CsvImportEngine: file size {Size} bytes exceeds limit {Limit} bytes — rejected.",
                csvStream.Length, opts.MaxFileSizeBytes);
            return new ImportResult
            {
                Status     = ImportStatus.HeaderValidationFailed,
                EntityType = profile.EntityTypeName,
                Errors     = [new RowError { RowNumber = 0, ColumnName = "(file)",
                    ErrorMessage = $"File size exceeds the maximum allowed size of {opts.MaxFileSizeBytes / 1_048_576} MB." }],
                Duration   = sw.Elapsed,
            };
        }

        // ── Header validation (AC-1) ──────────────────────────────────────
        var headers = await _parser.ReadHeadersAsync(csvStream, ct).ConfigureAwait(false);
        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);

        var missingColumns = profile.RequiredColumns
            .Where(col => !headerSet.Contains(col))
            .ToList();

        if (missingColumns.Count > 0)
        {
            _logger.LogWarning(
                "CsvImportEngine: header validation failed for {EntityType}. Missing={Missing}",
                profile.EntityTypeName, string.Join(", ", missingColumns));

            return new ImportResult
            {
                Status     = ImportStatus.HeaderValidationFailed,
                EntityType = profile.EntityTypeName,
                Errors     = missingColumns.Select(col => new RowError
                {
                    RowNumber    = 1,
                    ColumnName   = col,
                    ErrorMessage = $"Required column '{col}' is missing from the CSV header.",
                }).ToList(),
                Duration   = sw.Elapsed,
            };
        }

        // ── Streaming parse → validate → persist (AC-1, AC-2, AC-3, AC-4) ──
        if (csvStream.CanSeek)
            csvStream.Seek(0, SeekOrigin.Begin);

        var errors           = new List<RowError>();
        var skippedDuplicates = new List<string>();
        int totalRows        = 0;
        int successCount     = 0;
        int duplicateCount   = 0;
        bool aborted         = false;
        int batchNumber      = 0;
        var batchStopwatches = new List<long>(); // batch durations in ms for ETA

        // Pending batch: (row, entity) pairs awaiting SaveChangesAsync
        var batch = new List<(Dictionary<string, string> Row, T Entity)>();

        await foreach (var (lineNumber, fields) in _parser.ParseAsync(csvStream, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            totalRows++;

            // Row-level validation (AC-1, AC-3)
            var rowErrors = profile.ValidateRow(fields, lineNumber);
            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
                if (errors.Count >= opts.MaxErrorsBeforeAbort)
                {
                    aborted = true;
                    _logger.LogWarning(
                        "CsvImportEngine: error threshold {Threshold} reached at row {Row} — aborting.",
                        opts.MaxErrorsBeforeAbort, lineNumber);
                    break;
                }
                continue;
            }

            // Map to entity
            var entity = profile.MapRow(fields);

            // FK resolution for Appointment (special case — resolve patient_email → PatientId)
            if (entity is Appointment appointment && profile is AppointmentImportProfile apptProfile)
            {
                var fkErrors = new List<RowError>();
                var resolved = await apptProfile.TryResolvePatientIdAsync(
                    appointment, fields, _context, fkErrors, lineNumber, ct).ConfigureAwait(false);

                if (!resolved)
                {
                    errors.AddRange(fkErrors);
                    continue;
                }
            }

            // Duplicate detection (AC-4)
            bool isDuplicate = await profile.IsDuplicateAsync(entity, _context, ct).ConfigureAwait(false);
            if (isDuplicate)
            {
                duplicateCount++;
                var dupKey = profile.DuplicateKey(entity);
                skippedDuplicates.Add(dupKey);
                _logger.LogDebug(
                    "CsvImportEngine: duplicate skipped at row {Row}: {Key}",
                    lineNumber, dupKey);
                continue;
            }

            batch.Add((fields, entity));
            _context.Set<T>().Add(entity);

            // Flush batch when batch size is reached
            if (batch.Count >= opts.BatchSize)
            {
                var batchSw = Stopwatch.StartNew();
                successCount += await FlushBatchAsync(batch, errors, lineNumber, ct).ConfigureAwait(false);
                batchSw.Stop();
                batchNumber++;
                batchStopwatches.Add(batchSw.ElapsedMilliseconds);
                batch.Clear();

                if (onProgress is not null)
                {
                    TimeSpan? eta = null;
                    if (batchStopwatches.Count > 0 && estimatedBatches > batchNumber)
                    {
                        var avgMs     = batchStopwatches.Average();
                        var remaining = estimatedBatches - batchNumber;
                        eta = TimeSpan.FromMilliseconds(avgMs * remaining);
                    }

                    onProgress(new ImportProgress
                    {
                        CurrentBatch            = batchNumber,
                        TotalEstimatedBatches   = estimatedBatches,
                        ProcessedRows           = totalRows,
                        SuccessSoFar            = successCount,
                        ErrorsSoFar             = errors.Count,
                        DuplicatesSoFar         = duplicateCount,
                        PercentComplete         = estimatedBatches > 0
                            ? Math.Min(100.0, (double)batchNumber / estimatedBatches * 100)
                            : 0,
                        EstimatedTimeRemaining  = eta,
                    });
                }
            }
        }

        // Flush remaining rows
        if (batch.Count > 0 && !aborted)
            successCount += await FlushBatchAsync(batch, errors, totalRows + 1, ct).ConfigureAwait(false);

        sw.Stop();

        var status = aborted
            ? ImportStatus.Aborted
            : errors.Count > 0
                ? ImportStatus.CompletedWithErrors
                : ImportStatus.Completed;

        _logger.LogInformation(
            "CSV_IMPORT_COMPLETE: EntityType={EntityType}, Status={Status}, " +
            "TotalRows={Total}, Success={Success}, Errors={Errors}, Duplicates={Dups}, Duration={Duration}",
            profile.EntityTypeName, status, totalRows, successCount, errors.Count, duplicateCount, sw.Elapsed);

        return new ImportResult
        {
            Status            = status,
            EntityType        = profile.EntityTypeName,
            TotalRows         = totalRows,
            SuccessCount      = successCount,
            ErrorCount        = errors.Count,
            DuplicateCount    = duplicateCount,
            Errors            = errors,
            SkippedDuplicates = skippedDuplicates,
            Duration          = sw.Elapsed,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Batch persistence helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <c>SaveChangesAsync</c> for the current batch. On failure, clears the change
    /// tracker and adds a batch-level error entry for each row in the batch.
    /// Returns the count of successfully persisted rows.
    /// </summary>
    private async Task<int> FlushBatchAsync<T>(
        List<(Dictionary<string, string> Row, T Entity)> batch,
        List<RowError> errors,
        int lastLineNumber,
        CancellationToken ct)
        where T : class
    {
        if (batch.Count == 0)
            return 0;

        try
        {
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            return batch.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CsvImportEngine: batch save failed for {Count} row(s) ending at line ~{Line}.",
                batch.Count, lastLineNumber);

            // Discard all tracked changes so subsequent batches are unaffected.
            _context.ChangeTracker.Clear();

            errors.Add(new RowError
            {
                RowNumber    = lastLineNumber,
                ColumnName   = "(batch)",
                ErrorMessage = $"Batch of {batch.Count} row(s) could not be saved: {ex.Message}",
            });

            return 0;
        }
    }
}
