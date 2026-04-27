using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using UPACIP.Service.Caching;
using UPACIP.Service.Rag.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag;

/// <summary>
/// Scoped implementation of <see cref="IRagRetrievalService"/> that orchestrates parallel
/// cosine-similarity (or hybrid) searches across embedding categories and applies
/// threshold filtering and global top-K selection (US_077 AC-1, AC-2, AIR-R02).
///
/// Design decisions:
/// <list type="bullet">
///   <item>
///     <b>Parallel search</b>: all category searches are dispatched concurrently via
///     <see cref="Task.WhenAll"/> to meet the &lt;500ms latency target (AIR-R02).
///   </item>
///   <item>
///     <b>Defense-in-depth threshold</b>: results are re-filtered after aggregation even
///     though <see cref="IVectorSearchService"/> already applies the threshold per-table.
///   </item>
///   <item>
///     <b>Result cache</b>: fully assembled <see cref="RetrievalResult"/> is cached under
///     <c>rag:retrieval:{sha256(embedding+categories)}</c> with a 5-minute TTL (NFR-030,
///     AIR-O06). Cache entries are serialisable because all DTO properties are primitive/POCO.
///   </item>
///   <item>
///     <b>PII guardrail</b>: <see cref="RetrievalRequest.TextQuery"/> is never written to
///     structured log lines; only metadata (counts, latency, grounding status) is logged (AIR-S04).
///   </item>
/// </list>
/// </summary>
public sealed class RagRetrievalService : IRagRetrievalService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string GroundedStatus          = "grounded";
    private const string NoGroundingStatus       = "no-grounding-available";
    private const int    DefaultTopK             = 5;
    private const float  DefaultThreshold        = 0.75f;
    private static readonly TimeSpan CacheTtl    = TimeSpan.FromMinutes(5); // NFR-030

    /// <summary>All categories searched when <see cref="RetrievalRequest.TargetCategories"/> is null.</summary>
    private static readonly IReadOnlyList<EmbeddingCategory> AllCategories =
        [EmbeddingCategory.MedicalTerminology, EmbeddingCategory.IntakeTemplate, EmbeddingCategory.CodingGuideline];

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IVectorSearchService               _vectorSearch;
    private readonly IHybridSearchOrchestrator          _hybridOrchestrator;
    private readonly ICacheService                      _cache;
    private readonly ILogger<RagRetrievalService>       _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public RagRetrievalService(
        IVectorSearchService            vectorSearch,
        IHybridSearchOrchestrator       hybridOrchestrator,
        ICacheService                   cache,
        ILogger<RagRetrievalService>    logger)
    {
        _vectorSearch       = vectorSearch;
        _hybridOrchestrator = hybridOrchestrator;
        _cache              = cache;
        _logger             = logger;
    }

    // ── IRagRetrievalService ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RetrievalResult> RetrieveContextAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.QueryEmbedding.Length == 0)
            throw new ArgumentException("QueryEmbedding must not be empty.", nameof(request));

        var categories = request.TargetCategories is { Count: > 0 }
            ? request.TargetCategories
            : AllCategories;

        // ── Cache check ───────────────────────────────────────────────────────
        var cacheKey = BuildCacheKey(request.QueryEmbedding, categories);
        var cached   = await _cache.GetAsync<RetrievalResult>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogInformation(
                "RAG retrieval cache hit. Categories={Categories} ChunkCount={Count} IsGrounded={IsGrounded}",
                string.Join(",", categories), cached.Chunks.Count, cached.IsGrounded);
            return cached;
        }

        // ── Search: hybrid orchestrator or direct cosine-similarity ─────────
        var sw = Stopwatch.StartNew();

        List<RetrievedChunk> filtered;
        int totalCandidates;

        if (request.UseHybridSearch && !string.IsNullOrWhiteSpace(request.TextQuery))
        {
            // Delegate to HybridSearchOrchestrator (deduplication, weighted scoring,
            // exact-match boosting all handled internally — US_078 AC-1, AC-2, AC-4).
            var hybridResults = await _hybridOrchestrator.HybridSearchAsync(
                request.QueryEmbedding,
                request.TextQuery,
                categories,
                request.TopK,
                request.SimilarityThreshold,
                cancellationToken);

            filtered        = hybridResults.ToList();
            totalCandidates = filtered.Count;
        }
        else
        {
            var searchTasks = categories
                .Select(cat => SearchCategoryAsync(cat, request, cancellationToken))
                .ToArray();

            var categoryResults = await Task.WhenAll(searchTasks);
            var allCandidates   = categoryResults.SelectMany(r => r).ToList();
            totalCandidates     = allCandidates.Count;

            filtered = allCandidates
                .Where(c => c.SimilarityScore >= request.SimilarityThreshold)
                .OrderByDescending(c => c.SimilarityScore)
                .Take(request.TopK)
                .ToList();
        }

        sw.Stop();

        var isGrounded      = filtered.Count > 0;
        var groundingStatus = isGrounded ? GroundedStatus : NoGroundingStatus;

        if (!isGrounded)
        {
            _logger.LogWarning(
                "No RAG context found above threshold {Threshold} for query. Proceeding without grounding.",
                request.SimilarityThreshold);
        }

        var result = new RetrievalResult
        {
            Chunks                   = filtered,
            IsGrounded               = isGrounded,
            GroundingStatus          = groundingStatus,
            TotalCandidatesEvaluated = totalCandidates,
            RetrievalLatency         = sw.Elapsed,
        };

        _logger.LogInformation(
            "RAG retrieval completed in {LatencyMs}ms, {ChunkCount} chunks returned, IsGrounded={IsGrounded}",
            sw.ElapsedMilliseconds, filtered.Count, isGrounded);

        // ── Cache result ──────────────────────────────────────────────────────
        await _cache.SetAsync(cacheKey, result, CacheTtl, cancellationToken);

        return result;
    }

    /// <inheritdoc/>
    public Task<RetrievalResult> RetrieveContextForCategoryAsync(
        EmbeddingCategory category,
        float[] queryEmbedding,
        int topK = DefaultTopK,
        float similarityThreshold = DefaultThreshold,
        CancellationToken cancellationToken = default)
        => RetrieveContextAsync(
            new RetrievalRequest
            {
                QueryEmbedding      = queryEmbedding,
                TextQuery           = string.Empty,
                TargetCategories    = [category],
                TopK                = topK,
                SimilarityThreshold = similarityThreshold,
            },
            cancellationToken);

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Searches a single <paramref name="category"/> via either cosine-similarity or hybrid
    /// search, and maps results to <see cref="RetrievedChunk"/> records.
    /// </summary>
    private async Task<IEnumerable<RetrievedChunk>> SearchCategoryAsync(
        EmbeddingCategory category,
        RetrievalRequest request,
        CancellationToken ct)
    {
        try
        {
            IReadOnlyList<VectorSearchResult> raw;

            if (request.UseHybridSearch && !string.IsNullOrWhiteSpace(request.TextQuery))
            {
                raw = await _vectorSearch.HybridSearchAsync(category, new HybridSearchRequest
                {
                    QueryEmbedding      = request.QueryEmbedding,
                    TextQuery           = request.TextQuery,
                    TopK                = request.TopK,
                    SimilarityThreshold = request.SimilarityThreshold,
                });
            }
            else
            {
                raw = await _vectorSearch.SearchSimilarAsync(
                    category,
                    request.QueryEmbedding,
                    topK: request.TopK,
                    similarityThreshold: request.SimilarityThreshold);
            }

            return raw.Select(r => new RetrievedChunk
            {
                Id                = r.Id,
                Content           = r.Content,
                SimilarityScore   = r.Similarity ?? r.CombinedScore ?? 0f,
                Category          = category,
                SourceAttribution = r.Content.Length > 80
                    ? string.Concat(r.Content.AsSpan(0, 80), "…")
                    : r.Content,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RAG retrieval failed for category {Category}. Results excluded.",
                category);
            return [];
        }
    }

    /// <summary>
    /// Builds a stable Redis cache key from a SHA-256 hash of the query embedding bytes
    /// concatenated with the sorted category names (prevents key collision across different
    /// category sets for the same embedding).
    /// </summary>
    private static string BuildCacheKey(
        float[] embedding,
        IReadOnlyList<EmbeddingCategory> categories)
    {
        // Serialise embedding to bytes deterministically.
        var embBytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, embBytes, 0, embBytes.Length);

        // Stable category suffix — sort so order of TargetCategories doesn't affect key.
        var catSuffix = string.Join(",", categories
            .Select(c => c.ToString())
            .OrderBy(s => s, StringComparer.Ordinal));

        var hash = SHA256.HashData(embBytes);
        return $"rag:retrieval:{Convert.ToHexString(hash).ToLowerInvariant()}:{catSuffix}";
    }
}
