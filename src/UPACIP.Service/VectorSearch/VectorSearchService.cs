using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;

namespace UPACIP.Service.VectorSearch;

/// <summary>
/// pgvector-backed implementation of <see cref="IVectorSearchService"/>.
///
/// Architecture decisions:
///   - Uses <see cref="NpgsqlDataSource"/> (injected singleton) for raw SQL so that
///     the Npgsql Vector type mapping configured via <c>UseVector()</c> is applied.
///   - Table names are resolved from a compile-time whitelist — never interpolated from
///     user input — preventing SQL injection (OWASP A03).
///   - Cosine similarity is expressed as <c>1 - (embedding &lt;=&gt; @vec)</c> so higher
///     values are more similar (range: 0–1).
///   - Hybrid search uses Reciprocal Rank Fusion (RRF, constant = 60) to combine vector
///     similarity ranks with PostgreSQL <c>ts_rank</c> full-text search ranks (AIR-R06).
/// </summary>
public sealed class VectorSearchService : IVectorSearchService
{
    private const int EmbeddingDimensions = 384;
    private const int RrfConstant = 60;

    /// <summary>
    /// Whitelist mapping category → (table name, SQL content expression).
    /// The content expression selects the human-readable text for the result row.
    /// Using a compile-time dictionary prevents SQL injection even when the table
    /// name is referenced in format strings (OWASP A03).
    /// </summary>
    private static readonly IReadOnlyDictionary<EmbeddingCategory, (string TableName, string ContentExpr)> TableMap =
        new Dictionary<EmbeddingCategory, (string, string)>
        {
            [EmbeddingCategory.MedicalTerminology] = ("medical_terminology_embeddings", "COALESCE(term, '')"),
            [EmbeddingCategory.IntakeTemplate]     = ("intake_template_embeddings",     "COALESCE(content, '')"),
            [EmbeddingCategory.CodingGuideline]    = ("coding_guideline_embeddings",    "COALESCE(guideline_text, '')"),
        };

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        NpgsqlDataSource dataSource,
        ILogger<VectorSearchService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Cosine similarity search
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<VectorSearchResult>> SearchSimilarAsync(
        EmbeddingCategory category,
        float[] queryEmbedding,
        int topK = 5,
        float similarityThreshold = 0.75f)
    {
        ValidateEmbeddingDimensions(queryEmbedding);
        ValidateTopK(topK);
        ValidateSimilarityThreshold(similarityThreshold);

        var (tableName, contentExpr) = GetTableInfo(category);

        // Table name comes from whitelist — safe to use in format string.
        var sql = $"""
            SELECT id::text,
                   {contentExpr} AS content,
                   1.0 - (embedding <=> @queryVector) AS similarity
            FROM   {tableName}
            WHERE  1.0 - (embedding <=> @queryVector) >= @threshold
            ORDER  BY embedding <=> @queryVector
            LIMIT  @topK
            """;

        var results = new List<VectorSearchResult>();

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("queryVector", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("threshold",   similarityThreshold);
        cmd.Parameters.AddWithValue("topK",        topK);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VectorSearchResult
            {
                Id         = Guid.Parse(reader.GetString(0)),
                Content    = reader.GetString(1),
                Similarity = reader.GetFloat(2),
            });
        }

        _logger.LogDebug(
            "SearchSimilarAsync [{Category}] returned {Count} results (threshold={Threshold}, topK={TopK})",
            category, results.Count, similarityThreshold, topK);

        return results;
    }

    // -------------------------------------------------------------------------
    // Hybrid search (vector + FTS, merged via Reciprocal Rank Fusion)
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
        EmbeddingCategory category,
        HybridSearchRequest request)
    {
        ValidateEmbeddingDimensions(request.QueryEmbedding);
        ValidateTopK(request.TopK);
        ValidateSimilarityThreshold(request.SimilarityThreshold);

        if (string.IsNullOrWhiteSpace(request.TextQuery))
            throw new ArgumentException("TextQuery must not be empty.", nameof(request));

        var (tableName, contentExpr) = GetTableInfo(category);

        // candidateTopK: the inner window from which both legs draw candidates.
        int candidateTopK = request.TopK * 2;

        // Table name and content expression come from whitelist — safe to format.
        var sql = $"""
            WITH vector_results AS (
                SELECT id,
                       {contentExpr}                                                         AS content,
                       1.0 - (embedding <=> @queryVector)                                    AS similarity,
                       ROW_NUMBER() OVER (ORDER BY embedding <=> @queryVector)               AS vec_rank
                FROM   {tableName}
                WHERE  1.0 - (embedding <=> @queryVector) >= @threshold
                LIMIT  @candidateTopK
            ),
            fts_results AS (
                SELECT id,
                       {contentExpr}                                                                               AS content,
                       ts_rank(content_tsv, plainto_tsquery('english', @textQuery))                                AS fts_rank,
                       ROW_NUMBER() OVER (ORDER BY ts_rank(content_tsv, plainto_tsquery('english', @textQuery)) DESC) AS text_rank
                FROM   {tableName}
                WHERE  content_tsv @@ plainto_tsquery('english', @textQuery)
                LIMIT  @candidateTopK
            )
            SELECT COALESCE(v.id, f.id)::text                                                               AS id,
                   COALESCE(v.content, f.content)                                                            AS content,
                   v.similarity,
                   f.fts_rank,
                   (1.0 / ({RrfConstant} + COALESCE(v.vec_rank,  1000))) +
                   (1.0 / ({RrfConstant} + COALESCE(f.text_rank, 1000)))                                     AS combined_score
            FROM       vector_results v
            FULL OUTER JOIN fts_results f ON v.id = f.id
            ORDER BY combined_score DESC
            LIMIT @topK
            """;

        var results = new List<VectorSearchResult>();

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("queryVector",    new Vector(request.QueryEmbedding));
        cmd.Parameters.AddWithValue("threshold",      request.SimilarityThreshold);
        cmd.Parameters.AddWithValue("textQuery",      request.TextQuery);
        cmd.Parameters.AddWithValue("candidateTopK",  candidateTopK);
        cmd.Parameters.AddWithValue("topK",           request.TopK);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VectorSearchResult
            {
                Id           = Guid.Parse(reader.GetString(0)),
                Content      = reader.GetString(1),
                Similarity   = reader.IsDBNull(2) ? null : reader.GetFloat(2),
                FtsRank      = reader.IsDBNull(3) ? null : reader.GetFloat(3),
                CombinedScore = reader.GetFloat(4),
            });
        }

        _logger.LogDebug(
            "HybridSearchAsync [{Category}] returned {Count} results (threshold={Threshold}, topK={TopK})",
            category, results.Count, request.SimilarityThreshold, request.TopK);

        return results;
    }

    // -------------------------------------------------------------------------
    // Upsert
    // -------------------------------------------------------------------------

    public async Task UpsertEmbeddingAsync(
        EmbeddingCategory category,
        Guid id,
        string content,
        float[] embedding,
        Dictionary<string, string>? metadata = null)
    {
        ValidateEmbeddingDimensions(embedding);

        metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var vector = new Vector(embedding);

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = conn.CreateCommand();

        switch (category)
        {
            case EmbeddingCategory.MedicalTerminology:
                cmd.CommandText = """
                    INSERT INTO medical_terminology_embeddings
                        (id, term, description, source, embedding, updated_at)
                    VALUES
                        (@id, @term, @description, @source, @embedding, NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        term        = EXCLUDED.term,
                        description = EXCLUDED.description,
                        source      = EXCLUDED.source,
                        embedding   = EXCLUDED.embedding,
                        updated_at  = NOW()
                    """;
                cmd.Parameters.AddWithValue("id",          id);
                cmd.Parameters.AddWithValue("term",        content);
                cmd.Parameters.AddWithValue("description", GetMetaOrNull(metadata, "description"));
                cmd.Parameters.AddWithValue("source",      GetMetaOrNull(metadata, "source"));
                cmd.Parameters.AddWithValue("embedding",   vector);
                break;

            case EmbeddingCategory.IntakeTemplate:
                cmd.CommandText = """
                    INSERT INTO intake_template_embeddings
                        (id, template_name, section, content, embedding, updated_at)
                    VALUES
                        (@id, @templateName, @section, @content, @embedding, NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        template_name = EXCLUDED.template_name,
                        section       = EXCLUDED.section,
                        content       = EXCLUDED.content,
                        embedding     = EXCLUDED.embedding,
                        updated_at    = NOW()
                    """;
                cmd.Parameters.AddWithValue("id",           id);
                cmd.Parameters.AddWithValue("templateName", GetMetaOrDefault(metadata, "templateName", "Unnamed Template"));
                cmd.Parameters.AddWithValue("section",      GetMetaOrNull(metadata, "section"));
                cmd.Parameters.AddWithValue("content",      content);
                cmd.Parameters.AddWithValue("embedding",    vector);
                break;

            case EmbeddingCategory.CodingGuideline:
                cmd.CommandText = """
                    INSERT INTO coding_guideline_embeddings
                        (id, code_system, code_value, guideline_text, embedding, updated_at)
                    VALUES
                        (@id, @codeSystem, @codeValue, @guidelineText, @embedding, NOW())
                    ON CONFLICT (id) DO UPDATE SET
                        code_system    = EXCLUDED.code_system,
                        code_value     = EXCLUDED.code_value,
                        guideline_text = EXCLUDED.guideline_text,
                        embedding      = EXCLUDED.embedding,
                        updated_at     = NOW()
                    """;
                cmd.Parameters.AddWithValue("id",           id);
                cmd.Parameters.AddWithValue("codeSystem",   GetMetaOrDefault(metadata, "codeSystem", "UNKNOWN"));
                cmd.Parameters.AddWithValue("codeValue",    GetMetaOrNull(metadata, "codeValue"));
                cmd.Parameters.AddWithValue("guidelineText", content);
                cmd.Parameters.AddWithValue("embedding",    vector);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown embedding category.");
        }

        await cmd.ExecuteNonQueryAsync();
        _logger.LogDebug("UpsertEmbeddingAsync [{Category}] id={Id}", category, id);
    }

    // -------------------------------------------------------------------------
    // Delete
    // -------------------------------------------------------------------------

    public async Task DeleteEmbeddingAsync(EmbeddingCategory category, Guid id)
    {
        var (tableName, _) = GetTableInfo(category);

        // tableName comes from the compile-time whitelist — safe to interpolate.
        var sql = $"DELETE FROM {tableName} WHERE id = @id";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var rows = await cmd.ExecuteNonQueryAsync();
        _logger.LogDebug("DeleteEmbeddingAsync [{Category}] id={Id} rowsAffected={Rows}", category, id, rows);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the table name and content SQL expression for <paramref name="category"/>
    /// from the compile-time whitelist. Throws <see cref="ArgumentOutOfRangeException"/>
    /// for unmapped values.
    /// </summary>
    private static (string TableName, string ContentExpr) GetTableInfo(EmbeddingCategory category)
    {
        if (!TableMap.TryGetValue(category, out var info))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown embedding category.");
        return info;
    }

    private static void ValidateEmbeddingDimensions(float[] embedding)
    {
        if (embedding is null || embedding.Length != EmbeddingDimensions)
            throw new ArgumentException(
                $"Embedding must have exactly {EmbeddingDimensions} dimensions. " +
                $"Received: {embedding?.Length.ToString() ?? "null"}.",
                nameof(embedding));
    }

    private static void ValidateTopK(int topK)
    {
        if (topK < 1 || topK > 100)
            throw new ArgumentOutOfRangeException(nameof(topK), topK,
                "topK must be between 1 and 100.");
    }

    private static void ValidateSimilarityThreshold(float threshold)
    {
        if (threshold < 0.0f || threshold > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold,
                "similarityThreshold must be between 0.0 and 1.0.");
    }

    private static object GetMetaOrNull(Dictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value) ? value : DBNull.Value;

    private static string GetMetaOrDefault(Dictionary<string, string> meta, string key, string fallback)
        => meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
