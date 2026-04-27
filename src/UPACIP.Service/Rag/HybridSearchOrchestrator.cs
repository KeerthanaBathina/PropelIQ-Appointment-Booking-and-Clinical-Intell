using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Rag.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag;

/// <summary>
/// Scoped implementation of <see cref="IHybridSearchOrchestrator"/> (US_078 AC-1, AC-2, AC-4).
///
/// Pipeline:
/// <list type="number">
///   <item>Validate options: <c>SemanticWeight + KeywordWeight == 1.0</c>.</item>
///   <item>Query one or all embedding category indexes in parallel via
///     <see cref="IVectorSearchService.HybridSearchAsync"/>.</item>
///   <item>Deduplicate by chunk <c>Id</c> — keep highest <c>CombinedScore</c> (AC-2).</item>
///   <item>Apply configurable weighted scoring (AC-1).</item>
///   <item>Apply exact-match boosting for whole-word query hits (edge case: exact code match).</item>
///   <item>Sort by final score descending and take top-K.</item>
/// </list>
///
/// PII guardrail (AIR-S04): <paramref name="textQuery"/> never appears in structured log fields;
/// only chunk IDs, counts, and scores are logged.
/// </summary>
public sealed class HybridSearchOrchestrator : IHybridSearchOrchestrator
{
    // ── Statics ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<EmbeddingCategory> AllCategories =
        [EmbeddingCategory.MedicalTerminology, EmbeddingCategory.IntakeTemplate, EmbeddingCategory.CodingGuideline];

    /// <summary>
    /// Tolerance for weight-sum equality check (float arithmetic imprecision).
    /// </summary>
    private const float WeightSumTolerance = 0.001f;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IVectorSearchService                   _vectorSearch;
    private readonly HybridSearchOptions                    _options;
    private readonly ILogger<HybridSearchOrchestrator>      _logger;

    // ── Constructor ───────────────────────────────────────────────────────────

    public HybridSearchOrchestrator(
        IVectorSearchService                vectorSearch,
        IOptions<HybridSearchOptions>       options,
        ILogger<HybridSearchOrchestrator>   logger)
    {
        _vectorSearch = vectorSearch;
        _options      = options.Value;
        _logger       = logger;

        // Validate at construction time — caught by startup validation or first DI resolution.
        var weightSum = _options.SemanticWeight + _options.KeywordWeight;
        if (MathF.Abs(weightSum - 1.0f) > WeightSumTolerance)
            throw new InvalidOperationException(
                $"HybridSearch options invalid: SemanticWeight ({_options.SemanticWeight}) + " +
                $"KeywordWeight ({_options.KeywordWeight}) must equal 1.0 (got {weightSum:F4}).");
    }

    // ── IHybridSearchOrchestrator ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetrievedChunk>> HybridSearchAsync(
        float[] queryEmbedding,
        string textQuery,
        IReadOnlyList<EmbeddingCategory>? targetCategories = null,
        int topK = 5,
        float similarityThreshold = 0.75f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        if (queryEmbedding.Length == 0)
            throw new ArgumentException("queryEmbedding must not be empty.", nameof(queryEmbedding));

        var categories = targetCategories is { Count: > 0 } ? targetCategories : AllCategories;

        // ── Step 1: Parallel hybrid search across categories ──────────────────
        // QueryCategoryAsync returns (VectorSearchResult, EmbeddingCategory) tuples.
        var searchTasks = categories
            .Select(cat => QueryCategoryAsync(cat, queryEmbedding, textQuery, topK, similarityThreshold, cancellationToken))
            .ToArray();

        var categoryResults = await Task.WhenAll(searchTasks);

        // Flatten: each element is a list of (Result, Category) tuples.
        var allRaw = categoryResults
            .SelectMany(results => results)
            .ToList();

        var totalCandidates = allRaw.Count;

        // ── Step 2: Deduplication by Id — keep highest CombinedScore ─────────
        // Group by the VectorSearchResult's Id; keep the tuple with the best score.
        var deduped = allRaw
            .GroupBy(entry => entry.Result.Id)
            .Select(g => g
                .OrderByDescending(entry => entry.Result.CombinedScore ?? entry.Result.Similarity ?? 0f)
                .First())
            .ToList();  // List<(VectorSearchResult Result, EmbeddingCategory Category)>

        _logger.LogInformation(
            "HybridSearch: {TotalCandidates} raw candidates → {DedupedCount} after deduplication. Categories={Categories}",
            totalCandidates, deduped.Count, string.Join(",", categories));

        // ── Step 3: Weighted scoring ──────────────────────────────────────────
        var maxFtsRank = deduped.Count > 0
            ? deduped.Max(entry => entry.Result.FtsRank ?? 0f)
            : 0f;

        // Produce a scored list carrying Result, Category, and FinalScore.
        var scored = deduped
            .Select(entry =>
            {
                var similarity = entry.Result.Similarity ?? 0f;
                var ftsNorm    = maxFtsRank > 0f
                    ? (entry.Result.FtsRank ?? 0f) / maxFtsRank
                    : 0f;
                var finalScore = (similarity * _options.SemanticWeight)
                               + (ftsNorm   * _options.KeywordWeight);
                return (entry.Result, entry.Category, FinalScore: finalScore);
            })
            .ToList();  // List<(VectorSearchResult Result, EmbeddingCategory Category, float FinalScore)>

        // ── Step 4: Exact-match boosting ──────────────────────────────────────
        var boostedScored = ApplyExactMatchBoosting(scored, textQuery);

        // ── Step 5: Sort by final score descending and take top-K ─────────────
        var topResults = boostedScored
            .OrderByDescending(x => x.FinalScore)
            .Take(topK)
            .Select(x => new RetrievedChunk
            {
                Id                = x.Result.Id,
                Content           = x.Result.Content,
                SimilarityScore   = x.FinalScore,   // expose the weighted+boosted score
                Category          = x.Category,
                SourceAttribution = x.Result.Content.Length > 80
                    ? string.Concat(x.Result.Content.AsSpan(0, 80), "…")
                    : x.Result.Content,
            })
            .ToList();

        _logger.LogInformation(
            "HybridSearch: {TopK} results returned (of {DedupedCount} deduplicated candidates).",
            topResults.Count, deduped.Count);

        return topResults;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<IReadOnlyList<(VectorSearchResult Result, EmbeddingCategory Category)>>
        QueryCategoryAsync(
            EmbeddingCategory category,
            float[] queryEmbedding,
            string textQuery,
            int topK,
            float similarityThreshold,
            CancellationToken ct)
    {
        try
        {
            var results = await _vectorSearch.HybridSearchAsync(
                category,
                new HybridSearchRequest
                {
                    QueryEmbedding      = queryEmbedding,
                    TextQuery           = textQuery,
                    TopK                = topK,
                    SimilarityThreshold = similarityThreshold,
                });

            return results
                .Select(r => (Result: r, Category: category))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "HybridSearch: query failed for category {Category}. Results excluded.",
                category);
            return [];
        }
    }

    private List<(VectorSearchResult Result, EmbeddingCategory Category, float FinalScore)>
        ApplyExactMatchBoosting(
            List<(VectorSearchResult Result, EmbeddingCategory Category, float FinalScore)> scored,
            string textQuery)
    {
        if (!_options.EnableExactMatchBoosting || string.IsNullOrWhiteSpace(textQuery))
            return scored;

        Regex? exactPattern = null;
        try
        {
            var escapedQuery = Regex.Escape(textQuery.Trim());
            exactPattern = new Regex(
                $@"\b{escapedQuery}\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled,
                matchTimeout: TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "HybridSearch: could not compile exact-match pattern. Boosting skipped.");
            return scored;
        }

        return scored
            .Select(item =>
            {
                var score = item.FinalScore;
                try
                {
                    if (exactPattern.IsMatch(item.Result.Content))
                    {
                        score *= _options.ExactMatchBoostFactor;
                        _logger.LogInformation(
                            "Exact match boost applied to chunk {ChunkId}.",
                            item.Result.Id);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    _logger.LogWarning(
                        "HybridSearch: exact-match regex timed out for chunk {ChunkId}. Boost skipped.",
                        item.Result.Id);
                }
                return (item.Result, item.Category, FinalScore: score);
            })
            .ToList();
    }
}
