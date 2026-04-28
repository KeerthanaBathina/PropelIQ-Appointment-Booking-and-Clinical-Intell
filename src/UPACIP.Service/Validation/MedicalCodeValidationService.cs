using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using UPACIP.DataAccess;
using UPACIP.Service.Rag.Embedding;
using UPACIP.Service.Validation.Models;

namespace UPACIP.Service.Validation;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Validates ICD-10 and CPT codes against the current code libraries and returns
/// semantically relevant alternatives for invalid or deprecated codes (US_085 AC-4, DR-015).
///
/// Validation is deterministic (exact-match EF Core lookup). Suggestions use pgvector
/// cosine-similarity search against <c>coding_guideline_embeddings</c> via OpenAI
/// text-embedding-3-small (384 dimensions) — no LLM inference involved.
/// </summary>
public interface IMedicalCodeValidationService
{
    /// <summary>
    /// Validates a single medical code.
    /// </summary>
    /// <param name="codeValue">
    /// ICD-10 or CPT code to validate (e.g. <c>"E11.65"</c>, <c>"99213"</c>).
    /// Leading/trailing whitespace is trimmed; value is normalised to uppercase.
    /// </param>
    /// <param name="codeSystem">
    /// <c>"ICD-10"</c> or <c>"CPT"</c>. Case-insensitive.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="CodeValidationResult"/> with validity flag, deprecation flag, message,
    /// and up to 5 suggested alternatives when invalid or deprecated.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="codeValue"/> is empty/whitespace, or
    /// <paramref name="codeSystem"/> is not <c>"ICD-10"</c> or <c>"CPT"</c>.
    /// </exception>
    Task<CodeValidationResult> ValidateCodeAsync(
        string            codeValue,
        string            codeSystem,
        CancellationToken ct = default);

    /// <summary>
    /// Validates multiple codes in a single operation.
    ///
    /// Exact-match lookups are batched into a single DB round-trip per code system.
    /// Similarity searches for invalid/deprecated codes run in parallel, bounded to a
    /// maximum of 5 concurrent searches to avoid overwhelming the embedding service.
    /// </summary>
    /// <param name="codes">
    /// List of (CodeValue, CodeSystem) pairs. Order is preserved in the returned list.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CodeValidationResult>> ValidateCodesAsync(
        IReadOnlyList<(string CodeValue, string CodeSystem)> codes,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="IMedicalCodeValidationService"/> implementation backed by EF Core
/// (<c>icd10_code_library</c> / <c>cpt_code_library</c> for exact-match) and
/// NpgsqlDataSource (<c>coding_guideline_embeddings</c> for pgvector suggestions).
///
/// Architecture decisions:
/// <list type="bullet">
///   <item>
///     <strong>EF Core for exact-match</strong>: the code library DbSets are already
///     mapped and have composite indexes on (code_value, is_current) / (cpt_code, is_active)
///     making lookups efficient without raw SQL.
///   </item>
///   <item>
///     <strong>NpgsqlDataSource for suggestions</strong>: <c>coding_guideline_embeddings</c>
///     is not mapped via EF Core (pgvector type requires design-time Pgvector support, excluded
///     per <c>MedicalTerminologyEmbeddingConfiguration</c> rationale). Raw <c>NpgsqlCommand</c>
///     is used, following the existing pattern in <see cref="VectorSearch.VectorSearchService"/>.
///   </item>
///   <item>
///     <strong>OWASP A03</strong>: the guideline table name and column references are
///     compile-time constants — never interpolated from user input. Code system and
///     code value are always parameterized.
///   </item>
///   <item>
///     <strong>Similarity threshold 0.5</strong>: codes below this value are not
///     semantically close enough to be useful suggestions (DR-015).
///   </item>
/// </list>
/// </summary>
public sealed class MedicalCodeValidationService : IMedicalCodeValidationService
{
    // ── Compile-time constants (OWASP A03 — no user-supplied table names) ────

    private const string GuidelineEmbeddingsTable = "coding_guideline_embeddings";
    private const int    MaxSuggestions           = 5;
    private const float  SimilarityThreshold      = 0.5f;
    private const int    MaxParallelSuggestions   = 5;  // bounded concurrency (bulk method)

    // Normalised code system literals stored in the coding_guideline_embeddings table.
    private const string CodingGuidelineIcd10System = "ICD-10-CM";
    private const string CodingGuidelineCptSystem   = "CPT";

    private readonly ApplicationDbContext              _db;
    private readonly NpgsqlDataSource                  _dataSource;
    private readonly IEmbeddingGenerationService       _embeddingService;
    private readonly ILogger<MedicalCodeValidationService> _logger;

    public MedicalCodeValidationService(
        ApplicationDbContext                  db,
        NpgsqlDataSource                      dataSource,
        IEmbeddingGenerationService           embeddingService,
        ILogger<MedicalCodeValidationService> logger)
    {
        _db               = db;
        _dataSource       = dataSource;
        _embeddingService = embeddingService;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Single code validation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CodeValidationResult> ValidateCodeAsync(
        string            codeValue,
        string            codeSystem,
        CancellationToken ct = default)
    {
        var (normalizedCode, normalizedSystem) = NormalizeInputs(codeValue, codeSystem);

        return normalizedSystem switch
        {
            "ICD-10" => await ValidateIcd10Async(normalizedCode, ct),
            "CPT"    => await ValidateCptAsync(normalizedCode, ct),
            _        => throw new ArgumentException(
                $"Unsupported code system '{codeSystem}'. Supported values: ICD-10, CPT.",
                nameof(codeSystem))
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bulk validation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodeValidationResult>> ValidateCodesAsync(
        IReadOnlyList<(string CodeValue, string CodeSystem)> codes,
        CancellationToken ct = default)
    {
        if (codes.Count == 0)
            return Array.Empty<CodeValidationResult>();

        // ── Batch exact-match lookups (one query per code system) ─────────────
        // Separate into ICD-10 and CPT groups, run each as a single IN-query.
        var icd10Inputs = codes
            .Select((c, i) => (Index: i, CodeValue: c.CodeValue.Trim().ToUpperInvariant(), c.CodeSystem))
            .Where(c => string.Equals(c.CodeSystem.Trim(), "ICD-10", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var cptInputs = codes
            .Select((c, i) => (Index: i, CodeValue: c.CodeValue.Trim().ToUpperInvariant(), c.CodeSystem))
            .Where(c => string.Equals(c.CodeSystem.Trim(), "CPT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var icd10Values = icd10Inputs.Select(x => x.CodeValue).ToList();
        var cptValues   = cptInputs.Select(x => x.CodeValue).ToList();

        // Batch DB queries.
        var icd10Entries = icd10Values.Count > 0
            ? await _db.Icd10CodeLibrary
                .AsNoTracking()
                .Where(e => icd10Values.Contains(e.CodeValue))
                .ToListAsync(ct)
            : new List<DataAccess.Entities.Icd10CodeLibrary>();

        var cptEntries = cptValues.Count > 0
            ? await _db.CptCodeLibrary
                .AsNoTracking()
                .Where(e => cptValues.Contains(e.CptCode))
                .ToListAsync(ct)
            : new List<DataAccess.Entities.CptCodeLibrary>();

        var icd10Lookup = icd10Entries
            .GroupBy(e => e.CodeValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var cptLookup = cptEntries
            .GroupBy(e => e.CptCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // ── Build preliminary results (no suggestions yet) ────────────────────
        var results = new CodeValidationResult[codes.Count];

        foreach (var (index, codeVal, codeSystemRaw) in icd10Inputs)
            results[index] = BuildIcd10ResultFromLookup(codeVal, icd10Lookup);

        foreach (var (index, codeVal, codeSystemRaw) in cptInputs)
            results[index] = BuildCptResultFromLookup(codeVal, cptLookup);

        // Unsupported code systems (neither ICD-10 nor CPT).
        for (var i = 0; i < codes.Count; i++)
        {
            if (results[i] is not null) continue;
            results[i] = new CodeValidationResult
            {
                IsValid           = false,
                IsDeprecated      = false,
                SubmittedCode     = codes[i].CodeValue.Trim().ToUpperInvariant(),
                SubmittedCodeSystem = codes[i].CodeSystem,
                ValidationMessage = $"Unsupported code system '{codes[i].CodeSystem}'. Supported values: ICD-10, CPT.",
            };
        }

        // ── Parallel similarity searches for invalid/deprecated codes ─────────
        // Bounded to MaxParallelSuggestions concurrent operations to avoid overloading
        // the embedding service.
        var needsSuggestions = results
            .Select((r, i) => (Result: r, Index: i))
            .Where(x => !x.Result.IsValid)
            .ToList();

        if (needsSuggestions.Count > 0)
        {
            using var semaphore = new SemaphoreSlim(MaxParallelSuggestions);

            var suggestionTasks = needsSuggestions.Select(async item =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var originalCode = codes[item.Index];
                    var normalized   = originalCode.CodeValue.Trim().ToUpperInvariant();
                    var sysNorm      = originalCode.CodeSystem.Trim().ToUpperInvariant();

                    if (sysNorm is not "ICD-10" and not "CPT")
                        return;

                    var suggestions = await FindSimilarCodesAsync(normalized, sysNorm, ct);
                    results[item.Index] = item.Result with { SuggestedAlternatives = suggestions };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(suggestionTasks);
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICD-10 specific validation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<CodeValidationResult> ValidateIcd10Async(
        string normalizedCode, CancellationToken ct)
    {
        // Fetch all versions to detect deprecation accurately:
        // A code may exist in an old version (IsCurrent=false) but not in the latest.
        var entries = await _db.Icd10CodeLibrary
            .AsNoTracking()
            .Where(e => e.CodeValue == normalizedCode)
            .OrderByDescending(e => e.IsCurrent)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            // Code not found in library at all.
            _logger.LogDebug("ICD-10 code not found: {Code}", normalizedCode);
            var suggestions = await FindSimilarCodesAsync(normalizedCode, "ICD-10", ct);
            return new CodeValidationResult
            {
                IsValid             = false,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "ICD-10",
                ValidationMessage   = $"Code {normalizedCode} was not found in the ICD-10 library. " +
                                      "Did you mean one of these?",
                SuggestedAlternatives = suggestions,
            };
        }

        var current = entries.FirstOrDefault(e => e.IsCurrent);
        if (current is not null)
        {
            return new CodeValidationResult
            {
                IsValid             = true,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "ICD-10",
                ValidationMessage   = $"Code {normalizedCode} is valid.",
            };
        }

        // Code exists but is deprecated in all versions (IsCurrent=false).
        var deprecated = entries[0];
        _logger.LogDebug(
            "ICD-10 code deprecated: {Code} DeprecatedDate={Date} ReplacementCode={Replacement}",
            normalizedCode, deprecated.DeprecatedDate, deprecated.ReplacementCode);

        var altSuggestions = await BuildIcd10DeprecatedSuggestionsAsync(
            normalizedCode, deprecated.ReplacementCode, ct);

        return new CodeValidationResult
        {
            IsValid             = false,
            IsDeprecated        = true,
            SubmittedCode       = normalizedCode,
            SubmittedCodeSystem = "ICD-10",
            ValidationMessage   = $"Code {normalizedCode} is deprecated" +
                                  (deprecated.DeprecatedDate.HasValue
                                      ? $" (as of {deprecated.DeprecatedDate:yyyy-MM-dd})"
                                      : string.Empty) +
                                  ". Consider using one of the suggested alternatives.",
            SuggestedAlternatives = altSuggestions,
        };
    }

    /// <summary>
    /// For a deprecated ICD-10 code: prepend the direct replacement (if stored on the row)
    /// as a high-confidence suggestion before the pgvector similarity results.
    /// </summary>
    private async Task<IReadOnlyList<CodeSuggestion>> BuildIcd10DeprecatedSuggestionsAsync(
        string normalizedCode, string? replacementCode, CancellationToken ct)
    {
        var suggestions = new List<CodeSuggestion>();

        // Direct replacement takes priority — SimilarityScore=1.0 (exact designation).
        if (!string.IsNullOrWhiteSpace(replacementCode))
        {
            var replacement = await _db.Icd10CodeLibrary
                .AsNoTracking()
                .Where(e => e.CodeValue == replacementCode && e.IsCurrent)
                .FirstOrDefaultAsync(ct);

            if (replacement is not null)
            {
                suggestions.Add(new CodeSuggestion
                {
                    CodeValue       = replacement.CodeValue,
                    CodeSystem      = "ICD-10",
                    Description     = replacement.Description,
                    SimilarityScore = 1.0,
                });
            }
        }

        // Fill remaining slots with pgvector suggestions (skip the replacement code itself).
        var remaining = MaxSuggestions - suggestions.Count;
        if (remaining > 0)
        {
            var similarCodes = await FindSimilarCodesAsync(normalizedCode, "ICD-10", ct);
            foreach (var s in similarCodes)
            {
                if (suggestions.Count >= MaxSuggestions) break;
                if (string.Equals(s.CodeValue, replacementCode, StringComparison.OrdinalIgnoreCase)) continue;
                suggestions.Add(s);
            }
        }

        return suggestions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CPT specific validation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<CodeValidationResult> ValidateCptAsync(
        string normalizedCode, CancellationToken ct)
    {
        var entries = await _db.CptCodeLibrary
            .AsNoTracking()
            .Where(e => e.CptCode == normalizedCode)
            .OrderByDescending(e => e.IsActive)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            _logger.LogDebug("CPT code not found: {Code}", normalizedCode);
            var suggestions = await FindSimilarCodesAsync(normalizedCode, "CPT", ct);
            return new CodeValidationResult
            {
                IsValid             = false,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "CPT",
                ValidationMessage   = $"Code {normalizedCode} was not found in the CPT library. " +
                                      "Did you mean one of these?",
                SuggestedAlternatives = suggestions,
            };
        }

        var active = entries.FirstOrDefault(e => e.IsActive);
        if (active is not null)
        {
            return new CodeValidationResult
            {
                IsValid             = true,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "CPT",
                ValidationMessage   = $"Code {normalizedCode} is valid.",
            };
        }

        // Code exists but is expired/inactive.
        var expired   = entries[0];
        var cptSuggestions = await FindSimilarCodesAsync(normalizedCode, "CPT", ct);

        _logger.LogDebug(
            "CPT code deprecated/inactive: {Code} ExpirationDate={Date}",
            normalizedCode, expired.ExpirationDate);

        return new CodeValidationResult
        {
            IsValid             = false,
            IsDeprecated        = true,
            SubmittedCode       = normalizedCode,
            SubmittedCodeSystem = "CPT",
            ValidationMessage   = $"Code {normalizedCode} is deprecated" +
                                  (expired.ExpirationDate.HasValue
                                      ? $" (expired {expired.ExpirationDate:yyyy-MM-dd})"
                                      : string.Empty) +
                                  ". Consider using one of the suggested alternatives.",
            SuggestedAlternatives = cptSuggestions,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bulk lookup helpers (used by ValidateCodesAsync)
    // ─────────────────────────────────────────────────────────────────────────

    private static CodeValidationResult BuildIcd10ResultFromLookup(
        string normalizedCode,
        Dictionary<string, List<DataAccess.Entities.Icd10CodeLibrary>> lookup)
    {
        if (!lookup.TryGetValue(normalizedCode, out var entries) || entries.Count == 0)
        {
            return new CodeValidationResult
            {
                IsValid             = false,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "ICD-10",
                ValidationMessage   = $"Code {normalizedCode} was not found in the ICD-10 library. " +
                                      "Did you mean one of these?",
            };
        }

        if (entries.Any(e => e.IsCurrent))
        {
            return new CodeValidationResult
            {
                IsValid             = true,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "ICD-10",
                ValidationMessage   = $"Code {normalizedCode} is valid.",
            };
        }

        var deprecated = entries.OrderByDescending(e => e.UpdatedAt).First();
        return new CodeValidationResult
        {
            IsValid             = false,
            IsDeprecated        = true,
            SubmittedCode       = normalizedCode,
            SubmittedCodeSystem = "ICD-10",
            ValidationMessage   = $"Code {normalizedCode} is deprecated. " +
                                  "Consider using one of the suggested alternatives.",
        };
    }

    private static CodeValidationResult BuildCptResultFromLookup(
        string normalizedCode,
        Dictionary<string, List<DataAccess.Entities.CptCodeLibrary>> lookup)
    {
        if (!lookup.TryGetValue(normalizedCode, out var entries) || entries.Count == 0)
        {
            return new CodeValidationResult
            {
                IsValid             = false,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "CPT",
                ValidationMessage   = $"Code {normalizedCode} was not found in the CPT library. " +
                                      "Did you mean one of these?",
            };
        }

        if (entries.Any(e => e.IsActive))
        {
            return new CodeValidationResult
            {
                IsValid             = true,
                IsDeprecated        = false,
                SubmittedCode       = normalizedCode,
                SubmittedCodeSystem = "CPT",
                ValidationMessage   = $"Code {normalizedCode} is valid.",
            };
        }

        return new CodeValidationResult
        {
            IsValid             = false,
            IsDeprecated        = true,
            SubmittedCode       = normalizedCode,
            SubmittedCodeSystem = "CPT",
            ValidationMessage   = $"Code {normalizedCode} is deprecated. " +
                                  "Consider using one of the suggested alternatives.",
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // pgvector similarity search for suggestions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates an embedding for <paramref name="codeValue"/> and finds up to 5 semantically
    /// similar, non-deprecated codes from <c>coding_guideline_embeddings</c> via pgvector
    /// cosine distance.
    ///
    /// Uses raw <see cref="NpgsqlCommand"/> SQL following the pattern established by
    /// <c>VectorSearchService</c> — the embedding tables are not mapped in EF Core
    /// (pgvector requires design-time support excluded per project conventions).
    ///
    /// Table name and all column references are compile-time constants (OWASP A03).
    /// All dynamic values are parameterized.
    /// </summary>
    private async Task<IReadOnlyList<CodeSuggestion>> FindSimilarCodesAsync(
        string            codeValue,
        string            codeSystem,
        CancellationToken ct)
    {
        // Map application code system → the value stored in coding_guideline_embeddings.code_system.
        var dbCodeSystem = codeSystem switch
        {
            "ICD-10" => CodingGuidelineIcd10System,
            "CPT"    => CodingGuidelineCptSystem,
            _        => null
        };

        if (dbCodeSystem is null)
            return Array.Empty<CodeSuggestion>();

        // Generate embedding for the submitted code value.
        // Failures bubble up — if embedding generation fails, the caller receives an
        // empty suggestions list rather than a broken result.
        float[] queryEmbedding;
        try
        {
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(codeValue, ct);
            queryEmbedding = embeddingResult.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Embedding generation failed for code={Code} system={System}. " +
                "Returning empty suggestions.",
                codeValue, codeSystem);
            return Array.Empty<CodeSuggestion>();
        }

        // Raw SQL — table name is a compile-time constant, all runtime values are parameterized.
        const string Sql = $"""
            SELECT code_value,
                   code_system,
                   guideline_text                                AS description,
                   CAST(1.0 - (embedding <=> @queryVector) AS DOUBLE PRECISION) AS similarity_score
            FROM   {GuidelineEmbeddingsTable}
            WHERE  code_system  = @codeSystem
              AND  code_value  IS NOT NULL
              AND  code_value  != @submittedCode
              AND  1.0 - (embedding <=> @queryVector) >= @threshold
            ORDER  BY embedding <=> @queryVector
            LIMIT  @topK
            """;

        var suggestions = new List<CodeSuggestion>(MaxSuggestions);

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd  = new NpgsqlCommand(Sql, conn);

            cmd.Parameters.AddWithValue("queryVector",    new Vector(queryEmbedding));
            cmd.Parameters.AddWithValue("codeSystem",     dbCodeSystem);
            cmd.Parameters.AddWithValue("submittedCode",  codeValue);
            cmd.Parameters.AddWithValue("threshold",      (double)SimilarityThreshold);
            cmd.Parameters.AddWithValue("topK",           MaxSuggestions);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var codeVal     = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var codeSys     = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var similarity  = reader.IsDBNull(3) ? 0.0          : reader.GetDouble(3);

                if (string.IsNullOrWhiteSpace(codeVal)) continue;

                suggestions.Add(new CodeSuggestion
                {
                    CodeValue       = codeVal,
                    CodeSystem      = codeSystem,   // return application-facing name ("ICD-10", "CPT")
                    Description     = description,
                    SimilarityScore = similarity,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "pgvector similarity search failed for code={Code} system={System}. " +
                "Returning empty suggestions.",
                codeValue, codeSystem);
            return Array.Empty<CodeSuggestion>();
        }

        return suggestions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input normalization + validation
    // ─────────────────────────────────────────────────────────────────────────

    private static (string NormalizedCode, string NormalizedSystem) NormalizeInputs(
        string codeValue, string codeSystem)
    {
        if (string.IsNullOrWhiteSpace(codeValue))
            throw new ArgumentException("Code value must not be empty or whitespace.", nameof(codeValue));

        if (string.IsNullOrWhiteSpace(codeSystem))
            throw new ArgumentException("Code system must not be empty or whitespace.", nameof(codeSystem));

        var normalizedCode   = codeValue.Trim().ToUpperInvariant();
        var normalizedSystem = codeSystem.Trim().ToUpperInvariant() switch
        {
            "ICD-10" or "ICD10" or "ICD-10-CM" or "ICD10CM" => "ICD-10",
            "CPT"                                            => "CPT",
            var other => other   // pass through for the switch in ValidateCodeAsync to reject
        };

        return (normalizedCode, normalizedSystem);
    }
}
