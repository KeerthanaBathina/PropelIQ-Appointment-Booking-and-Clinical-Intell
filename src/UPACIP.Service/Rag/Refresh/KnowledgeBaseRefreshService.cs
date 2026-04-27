using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using UPACIP.Service.Rag.Chunking;
using UPACIP.Service.Rag.Chunking.Models;
using UPACIP.Service.Rag.Embedding;
using UPACIP.Service.Rag.Refresh.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Refresh;

/// <summary>
/// Scoped implementation of <see cref="IKnowledgeBaseRefreshService"/> (US_078 AC-3, AIR-R05).
///
/// Pipeline summary:
/// <list type="number">
///   <item>Sanitize + validate input.</item>
///   <item>Truncate staging table (idempotent re-run guard).</item>
///   <item>Diff incoming entries against live table by <c>code_value</c> / <c>term</c>.</item>
///   <item>Chunk + batch-embed new/updated codes; write to staging table via
///         <see cref="IVectorSearchService.UpsertEmbeddingAsync"/> pointing at the
///         staging table name through a per-call connection redirect.</item>
///   <item>Verify staging row count.</item>
///   <item>Atomic DDL swap: live → old, staging → live, drop old, recreate staging.</item>
///   <item>Soft-mark deprecated entries with <c>deprecated_at</c>.</item>
///   <item>Trigger async index rebuild (outside transaction).</item>
/// </list>
///
/// Security:
///   - Table names derived exclusively from the <see cref="EmbeddingCategory"/> enum switch
///     (compile-time whitelist) — never from caller-supplied strings (OWASP A03).
///   - <see cref="CodeLibraryEntry.CodeValue"/> and <see cref="CodeLibraryEntry.Description"/>
///     are sanitised (null bytes stripped, lengths capped) before embedding (AIR-O01).
///   - <see cref="KbRefreshRequest.InitiatedByUserId"/> is written only to audit log and
///     is never forwarded to OpenAI (AIR-S04).
///   - Last-known result is stored in memory for polling; no PII in the result object.
/// </summary>
public sealed class KnowledgeBaseRefreshService : IKnowledgeBaseRefreshService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int    MaxCodeValueLength   = 20;
    private const int    MaxDescriptionLength = 4_000;

    // ── Compile-time table-name whitelist (OWASP A03) ─────────────────────────

    private static readonly IReadOnlyDictionary<EmbeddingCategory, (string Live, string Staging)> TableNames =
        new Dictionary<EmbeddingCategory, (string, string)>
        {
            [EmbeddingCategory.MedicalTerminology] = ("medical_terminology_embeddings",
                                                      "medical_terminology_embeddings_staging"),
            [EmbeddingCategory.IntakeTemplate]     = ("intake_template_embeddings",
                                                      "intake_template_embeddings_staging"),
            [EmbeddingCategory.CodingGuideline]    = ("coding_guideline_embeddings",
                                                      "coding_guideline_embeddings_staging"),
        };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IDocumentChunkingService           _chunker;
    private readonly IEmbeddingGenerationService        _embedder;
    private readonly IVectorSearchService               _vectorSearch;
    private readonly NpgsqlDataSource                   _dataSource;
    private readonly ILogger<KnowledgeBaseRefreshService> _logger;

    // ── In-memory last-result for polling ─────────────────────────────────────
    // Volatile so the read in GetRefreshStatusAsync sees the latest write
    // without a full lock.  Full result-object replacement is atomic on 64-bit
    // (reference assignment), which is sufficient for a simple polling use case.

    private volatile KbRefreshResult? _lastResult;

    // ── Constructor ───────────────────────────────────────────────────────────

    public KnowledgeBaseRefreshService(
        IDocumentChunkingService            chunker,
        IEmbeddingGenerationService         embedder,
        IVectorSearchService                vectorSearch,
        NpgsqlDataSource                    dataSource,
        ILogger<KnowledgeBaseRefreshService> logger)
    {
        _chunker      = chunker;
        _embedder     = embedder;
        _vectorSearch = vectorSearch;
        _dataSource   = dataSource;
        _logger       = logger;
    }

    // ── IKnowledgeBaseRefreshService ──────────────────────────────────────────

    /// <inheritdoc/>
    public Task<KbRefreshResult?> GetRefreshStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_lastResult);

    /// <inheritdoc/>
    public async Task<KbRefreshResult> RefreshAsync(
        KbRefreshRequest  request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "KbRefresh: starting. Category={Category} Version={Version} EntryCount={Count} TriggeredBy={User}",
            request.TargetCategory, request.SourceVersion, request.Entries.Count,
            request.InitiatedByUserId);  // UserId is non-PII admin identity (AIR-S04)

        if (!TableNames.TryGetValue(request.TargetCategory, out var tables))
        {
            return Fail(sw, $"Unsupported target category: {request.TargetCategory}.");
        }

        try
        {
            // ── Step 0: sanitise inputs ──────────────────────────────────────
            var sanitisedEntries = SanitiseEntries(request.Entries);

            // ── Step 1: truncate staging (idempotent re-run guard) ───────────
            await ExecuteSqlAsync(
                $"TRUNCATE TABLE {tables.Staging}",
                cancellationToken);

            _logger.LogInformation("KbRefresh: staging table {Staging} truncated.", tables.Staging);

            // ── Step 2: load existing live codes for diff ────────────────────
            var existingCodes = await LoadExistingCodesAsync(tables.Live, cancellationToken);

            // ── Step 3: diff ─────────────────────────────────────────────────
            var (toEmbed, toDeprecate, unchanged) = DiffEntries(sanitisedEntries, existingCodes);

            _logger.LogInformation(
                "KbRefresh diff: {New} new, {Updated} updated, {Deprecated} to deprecate, {Unchanged} unchanged.",
                toEmbed.Count(e => !existingCodes.ContainsKey(e.CodeValue)),
                toEmbed.Count(e =>  existingCodes.ContainsKey(e.CodeValue)),
                toDeprecate.Count,
                unchanged);

            // ── Step 4: chunk + embed + write to staging ─────────────────────
            int newCount     = 0;
            int updatedCount = 0;

            foreach (var entry in toEmbed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var isNew = !existingCodes.ContainsKey(entry.CodeValue);

                await ChunkAndWriteToStagingAsync(entry, tables.Staging, cancellationToken);

                if (isNew) newCount++;
                else       updatedCount++;
            }

            // ── Step 5: verify staging row count ─────────────────────────────
            var stagingCount = await CountRowsAsync(tables.Staging, cancellationToken);

            _logger.LogInformation(
                "KbRefresh: staging verification — {Count} rows in {Table}.",
                stagingCount, tables.Staging);

            // Allow zero if the entire refresh set was unchanged / deprecated only.
            if (toEmbed.Count > 0 && stagingCount == 0)
            {
                return Fail(sw,
                    $"Staging table {tables.Staging} has 0 rows after embedding {toEmbed.Count} entries.");
            }

            // ── Step 6: atomic swap ───────────────────────────────────────────
            await AtomicSwapAsync(tables.Live, tables.Staging, cancellationToken);

            _logger.LogInformation(
                "KbRefresh: atomic swap complete. {Live} now contains {Count} rows.",
                tables.Live, stagingCount);

            // ── Step 7: soft-mark deprecated entries ──────────────────────────
            int deprecatedCount = 0;
            if (toDeprecate.Count > 0)
            {
                deprecatedCount = await MarkDeprecatedAsync(tables.Live, toDeprecate, cancellationToken);
                _logger.LogInformation(
                    "KbRefresh: {Count} entries marked deprecated in {Live}.",
                    deprecatedCount, tables.Live);
            }

            // ── Step 8: rebuild indexes (best-effort, outside transaction) ────
            await TryRebuildIndexesAsync(request.TargetCategory, cancellationToken);

            sw.Stop();

            var result = new KbRefreshResult
            {
                NewCodesAdded  = newCount,
                CodesUpdated   = updatedCount,
                CodesDeprecated = deprecatedCount,
                TotalProcessed  = sanitisedEntries.Count,
                Duration        = sw.Elapsed,
                Status          = RefreshStatus.Completed,
            };

            _lastResult = result;

            _logger.LogInformation(
                "KbRefresh: completed. New={New} Updated={Updated} Deprecated={Deprecated} " +
                "Duration={Duration}ms Version={Version}",
                newCount, updatedCount, deprecatedCount, sw.ElapsedMilliseconds, request.SourceVersion);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("KbRefresh: cancelled. Version={Version}", request.SourceVersion);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "KbRefresh: failed. Version={Version} Elapsed={Elapsed}ms. Live table unchanged.",
                request.SourceVersion, sw.ElapsedMilliseconds);

            return Fail(sw, ex.Message);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Sanitises each entry: strips null bytes, caps lengths.
    /// Guards against OWASP A03 injection via the downstream embedding API call.
    /// </summary>
    private static IReadOnlyList<CodeLibraryEntry> SanitiseEntries(
        IReadOnlyList<CodeLibraryEntry> entries)
    {
        return entries
            .Select(e => e with
            {
                CodeValue   = Sanitise(e.CodeValue,   MaxCodeValueLength),
                Description = Sanitise(e.Description, MaxDescriptionLength),
            })
            .ToList();
    }

    private static string Sanitise(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        // Strip null bytes (mirrors EmbeddingGenerationService.SanitizeText pattern)
        var clean = value.Replace("\0", "", StringComparison.Ordinal);
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    /// <summary>
    /// Loads the current live table's code-value → description map.
    /// Uses the category-appropriate column name via a compile-time switch.
    /// </summary>
    private async Task<Dictionary<string, string>> LoadExistingCodesAsync(
        string liveTable, CancellationToken ct)
    {
        // Table name is from the compile-time whitelist — safe to interpolate.
        var sql = liveTable switch
        {
            "medical_terminology_embeddings"  =>
                "SELECT term, COALESCE(description,'') FROM medical_terminology_embeddings WHERE deprecated_at IS NULL",
            "intake_template_embeddings"      =>
                "SELECT template_name, COALESCE(content,'') FROM intake_template_embeddings WHERE deprecated_at IS NULL",
            "coding_guideline_embeddings"     =>
                "SELECT code_value, COALESCE(guideline_text,'') FROM coding_guideline_embeddings WHERE deprecated_at IS NULL",
            _ => throw new InvalidOperationException($"Unknown live table: {liveTable}"),
        };

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var conn   = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var key   = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (!string.IsNullOrEmpty(key))
                result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Classifies entries into: to-embed (new+updated) and to-deprecate.
    /// Unchanged entries are counted for logging.
    /// </summary>
    private static (List<CodeLibraryEntry> ToEmbed, List<string> ToDeprecate, int Unchanged)
        DiffEntries(
            IReadOnlyList<CodeLibraryEntry>     incoming,
            Dictionary<string, string>          existing)
    {
        var toEmbed    = new List<CodeLibraryEntry>();
        var toDeprecate = new List<string>();
        var incomingKeys = new HashSet<string>(
            incoming.Select(e => e.CodeValue),
            StringComparer.OrdinalIgnoreCase);

        int unchanged = 0;

        foreach (var entry in incoming)
        {
            if (entry.IsDeprecated)
            {
                toDeprecate.Add(entry.CodeValue);
                continue;
            }

            if (existing.TryGetValue(entry.CodeValue, out var existingDesc))
            {
                // Updated: description changed.
                if (!string.Equals(existingDesc, entry.Description, StringComparison.Ordinal))
                    toEmbed.Add(entry);
                else
                    unchanged++;
            }
            else
            {
                // New.
                toEmbed.Add(entry);
            }
        }

        // Codes in live but absent from incoming (and not explicitly flagged) → deprecate.
        foreach (var key in existing.Keys)
        {
            if (!incomingKeys.Contains(key))
                toDeprecate.Add(key);
        }

        return (toEmbed, toDeprecate, unchanged);
    }

    /// <summary>
    /// Chunks one code description, batch-embeds all chunks, then writes each chunk's
    /// embedding to the <paramref name="stagingTable"/> via raw Npgsql commands.
    ///
    /// Token budget (AIR-O01): the chunker enforces the 512-token window; the description
    /// cap of 4 000 chars guarantees at most ~6 chunks per code entry.
    ///
    /// Fallback (AIR-R05): if the embedding call throws (transient OpenAI error) the
    /// exception propagates to <see cref="RefreshAsync"/> which sets Status=Failed and
    /// leaves the live table untouched.
    /// </summary>
    private async Task ChunkAndWriteToStagingAsync(
        CodeLibraryEntry entry,
        string           stagingTable,
        CancellationToken ct)
    {
        // Chunk the description.
        var chunkResult = await _chunker.ChunkDocumentAsync(
            new ChunkingRequest
            {
                DocumentText     = entry.Description,
                SourceDocumentId = DeriveSourceId(entry.CodeValue),
                SourceName       = entry.CodeValue,
                Category         = entry.Category,
            },
            ct);

        if (chunkResult.TotalChunks == 0) return;

        // Batch embed all chunk texts.
        var chunkTexts = chunkResult.Chunks.Select(c => c.Content).ToList();
        var embedResults = await _embedder.GenerateEmbeddingsAsync(chunkTexts, ct);

        // Write each chunk + embedding directly to the staging table.
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        for (int i = 0; i < chunkResult.Chunks.Count && i < embedResults.Count; i++)
        {
            if (embedResults[i].Embedding.Length == 0) continue;

            var chunkId   = DeriveChunkId(entry.CodeValue, i);
            var embedding = new Pgvector.Vector(embedResults[i].Embedding);
            var chunkText = chunkResult.Chunks[i].Content;

            // SQL text is derived from the compile-time staging table name — safe.
            var sql = stagingTable switch
            {
                "medical_terminology_embeddings_staging" =>
                    """
                    INSERT INTO medical_terminology_embeddings_staging
                        (id, term, description, source, embedding, created_at, updated_at)
                    VALUES
                        (@id, @term, @desc, @source, @emb, NOW(), NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        term        = EXCLUDED.term,
                        description = EXCLUDED.description,
                        embedding   = EXCLUDED.embedding,
                        updated_at  = NOW()
                    """,

                "intake_template_embeddings_staging" =>
                    """
                    INSERT INTO intake_template_embeddings_staging
                        (id, template_name, section, content, embedding, created_at, updated_at)
                    VALUES
                        (@id, @term, @desc, @content, @emb, NOW(), NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        template_name = EXCLUDED.template_name,
                        content       = EXCLUDED.content,
                        embedding     = EXCLUDED.embedding,
                        updated_at    = NOW()
                    """,

                "coding_guideline_embeddings_staging" =>
                    """
                    INSERT INTO coding_guideline_embeddings_staging
                        (id, code_system, code_value, guideline_text, embedding, created_at, updated_at)
                    VALUES
                        (@id, @codeSystem, @codeValue, @guideline, @emb, NOW(), NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        code_system    = EXCLUDED.code_system,
                        code_value     = EXCLUDED.code_value,
                        guideline_text = EXCLUDED.guideline_text,
                        embedding      = EXCLUDED.embedding,
                        updated_at     = NOW()
                    """,

                _ => throw new InvalidOperationException($"Unknown staging table: {stagingTable}"),
            };

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id",  chunkId);
            cmd.Parameters.AddWithValue("emb", embedding);

            // Map parameters to the correct column names by table.
            if (stagingTable == "medical_terminology_embeddings_staging")
            {
                cmd.Parameters.AddWithValue("term",   chunkText);
                cmd.Parameters.AddWithValue("desc",   entry.Description);
                cmd.Parameters.AddWithValue("source", entry.CodeSystem);
            }
            else if (stagingTable == "intake_template_embeddings_staging")
            {
                cmd.Parameters.AddWithValue("term",    entry.CodeValue);
                cmd.Parameters.AddWithValue("desc",    chunkText);     // template_name placeholder
                cmd.Parameters.AddWithValue("content", chunkText);
            }
            else
            {
                cmd.Parameters.AddWithValue("codeSystem", entry.CodeSystem);
                cmd.Parameters.AddWithValue("codeValue",  entry.CodeValue);
                cmd.Parameters.AddWithValue("guideline",  chunkText);
            }

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Executes the atomic DDL swap inside a single PostgreSQL transaction.
    /// If this transaction fails, the live table is untouched (DR-029 principle).
    ///
    /// Note: <c>REINDEX … CONCURRENTLY</c> is intentionally outside this transaction
    /// because PostgreSQL 16 prohibits concurrent reindex inside a transaction block.
    /// </summary>
    private async Task AtomicSwapAsync(string liveTable, string stagingTable, CancellationToken ct)
    {
        var oldTable = liveTable + "_old";

        // All table names are from the compile-time whitelist.
        var sql = $"""
            BEGIN;
            ALTER TABLE {liveTable}   RENAME TO {oldTable};
            ALTER TABLE {stagingTable} RENAME TO {liveTable};
            DROP TABLE  {oldTable};
            CREATE TABLE {stagingTable} (LIKE {liveTable} INCLUDING ALL);
            COMMIT;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Stamps <c>deprecated_at = NOW()</c> for all code values in <paramref name="codeValues"/>
    /// within the (already-swapped) live table.
    /// Uses a column determined by the table name — no dynamic SQL from user input.
    /// </summary>
    private async Task<int> MarkDeprecatedAsync(
        string            liveTable,
        List<string>      codeValues,
        CancellationToken ct)
    {
        var keyColumn = liveTable switch
        {
            "medical_terminology_embeddings" => "term",
            "intake_template_embeddings"     => "template_name",
            "coding_guideline_embeddings"    => "code_value",
            _                                => throw new InvalidOperationException($"Unknown live table: {liveTable}"),
        };

        // Parameterised IN list built with numbered placeholders — OWASP A03.
        var placeholders = string.Join(", ",
            codeValues.Select((_, i) => $"@p{i}"));

        var sql = $"UPDATE {liveTable} SET deprecated_at = NOW() " +
                  $"WHERE {keyColumn} = ANY(ARRAY[{placeholders}]) AND deprecated_at IS NULL";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);

        for (int i = 0; i < codeValues.Count; i++)
            cmd.Parameters.AddWithValue($"p{i}", codeValues[i]);

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Triggers <c>REINDEX INDEX CONCURRENTLY</c> for the IVFFlat and GIN indexes
    /// belonging to the refreshed table.  Errors are logged and swallowed because a
    /// missing or stale index degrades query performance but does not corrupt data.
    /// </summary>
    private async Task TryRebuildIndexesAsync(EmbeddingCategory category, CancellationToken ct)
    {
        // Index names from provision-pgvector.sql — safe to inline.
        var indexNames = category switch
        {
            EmbeddingCategory.MedicalTerminology =>
                new[] { "idx_medical_terminology_emb_ivfflat", "idx_medical_terminology_emb_tsv" },
            EmbeddingCategory.IntakeTemplate =>
                new[] { "idx_intake_template_emb_ivfflat", "idx_intake_template_emb_tsv" },
            EmbeddingCategory.CodingGuideline =>
                new[] { "idx_coding_guideline_emb_ivfflat", "idx_coding_guideline_emb_tsv" },
            _ => Array.Empty<string>(),
        };

        foreach (var idx in indexNames)
        {
            try
            {
                await ExecuteSqlAsync($"REINDEX INDEX CONCURRENTLY {idx}", ct);
                _logger.LogInformation("KbRefresh: rebuilt index {Index}.", idx);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "KbRefresh: index rebuild for {Index} failed (non-fatal). " +
                    "Run 'REINDEX INDEX CONCURRENTLY {Index}' manually.", idx);
            }
        }
    }

    private async Task<long> CountRowsAsync(string tableName, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand($"SELECT COUNT(*) FROM {tableName}", conn);
        var result           = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result);
    }

    private async Task ExecuteSqlAsync(string sql, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private KbRefreshResult Fail(Stopwatch sw, string errorMessage)
    {
        sw.Stop();
        var result = new KbRefreshResult
        {
            Status       = RefreshStatus.Failed,
            Duration     = sw.Elapsed,
            ErrorMessage = errorMessage,
        };
        _lastResult = result;
        return result;
    }

    /// <summary>
    /// Derives a deterministic, stable UUID for a code entry from its code value.
    /// Ensures repeated refreshes produce the same chunk IDs for the same code.
    /// </summary>
    private static Guid DeriveSourceId(string codeValue)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeValue));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static Guid DeriveChunkId(string codeValue, int chunkIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{codeValue}:{chunkIndex}"));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
