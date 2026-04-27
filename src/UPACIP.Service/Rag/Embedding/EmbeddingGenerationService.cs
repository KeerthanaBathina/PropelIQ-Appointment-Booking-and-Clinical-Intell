using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Caching;
using UPACIP.Service.Rag.Chunking;
using UPACIP.Service.Rag.Chunking.Models;
using UPACIP.Service.Rag.Embedding.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Embedding;

/// <summary>
/// Scoped implementation of <see cref="IEmbeddingGenerationService"/> that converts document
/// chunks into 384-dimensional vectors using OpenAI text-embedding-3-small and stores them in
/// the appropriate pgvector table (US_076 AC-2, AC-3, AC-4).
///
/// Design decisions:
/// <list type="bullet">
///   <item>
///     <b>Cache key</b>: <c>embedding:{sha256hex(text)}</c> — SHA-256 prevents collisions
///     and avoids embedding raw text in cache keys (AIR-O06, OWASP A02).
///   </item>
///   <item>
///     <b>Batch size</b>: max 100 texts per OpenAI API call (API limit). Larger sets are
///     split into sub-batches processed sequentially.
///   </item>
///   <item>
///     <b>Retry</b>: Polly V8 pipeline — 3 retries, exponential backoff with jitter (AIR-O08).
///   </item>
///   <item>
///     <b>Dimension validation</b>: each vector is checked for exactly 384 dimensions.
///     A mismatch throws <see cref="InvalidOperationException"/> which triggers retry.
///   </item>
///   <item>
///     <b>PII guardrail</b>: actual text content is never written to structured logs (AIR-S04).
///     Only IDs, counts, and token usage are logged.
///   </item>
///   <item>
///     <b>Input sanitisation</b>: null bytes removed before API call (OWASP A03 injection prevention).
///   </item>
/// </list>
/// </summary>
public sealed class EmbeddingGenerationService : IEmbeddingGenerationService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int    EmbeddingDimensions = 384;
    private const int    MaxBatchSize        = 100;
    private const string EmbeddingModel      = "text-embedding-3-small";

    /// <summary>24-hour TTL for embedding cache entries (AIR-O06).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive    = true,
        DefaultIgnoreCondition         = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IHttpClientFactory                     _httpClientFactory;
    private readonly AiGatewaySettings                      _settings;
    private readonly ICacheService                          _cache;
    private readonly IVectorSearchService                   _vectorSearch;
    private readonly IDocumentChunkingService               _chunking;
    private readonly ILogger<EmbeddingGenerationService>    _logger;

    /// <summary>
    /// Per-request Polly V8 retry pipeline — 3 retries, exponential backoff with jitter
    /// on all exceptions except <see cref="OperationCanceledException"/> (AIR-O08).
    /// </summary>
    private readonly ResiliencePipeline _retryPipeline;

    // ── Constructor ───────────────────────────────────────────────────────────

    public EmbeddingGenerationService(
        IHttpClientFactory                      httpClientFactory,
        IOptions<AiGatewaySettings>             settings,
        ICacheService                           cache,
        IVectorSearchService                    vectorSearch,
        IDocumentChunkingService                chunking,
        ILogger<EmbeddingGenerationService>     logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings          = settings.Value;
        _cache             = cache;
        _vectorSearch      = vectorSearch;
        _chunking          = chunking;
        _logger            = logger;

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(2),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                ShouldHandle     = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry          = args =>
                {
                    _logger.LogWarning(
                        "EmbeddingGenerationService: retry #{Attempt}/3 after {Delay:g}. Reason={Message}",
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message);
                    return default;
                },
            })
            .Build();
    }

    // ── IEmbeddingGenerationService ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<EmbeddingResult> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var sanitized = SanitizeText(text);
        var cacheKey  = BuildCacheKey(sanitized);

        var cached = await _cache.GetAsync<CachedEmbedding>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return new EmbeddingResult { Embedding = cached.Vector, TokensUsed = 0, WasCached = true };
        }

        var (vectors, totalTokens) = await CallEmbeddingApiAsync([sanitized], cancellationToken);

        await _cache.SetAsync(cacheKey, new CachedEmbedding(vectors[0]), CacheTtl, cancellationToken);

        return new EmbeddingResult
        {
            Embedding  = vectors[0],
            TokensUsed = totalTokens,
            WasCached  = false,
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var results          = new EmbeddingResult[texts.Count];
        var sanitizedTexts   = texts.Select(SanitizeText).ToArray();
        var uncachedIndices  = new List<int>(texts.Count);

        // ── Phase 1: Check cache for each text ───────────────────────────────
        for (var i = 0; i < sanitizedTexts.Length; i++)
        {
            var cached = await _cache.GetAsync<CachedEmbedding>(
                BuildCacheKey(sanitizedTexts[i]), cancellationToken);

            if (cached is not null)
                results[i] = new EmbeddingResult { Embedding = cached.Vector, TokensUsed = 0, WasCached = true };
            else
                uncachedIndices.Add(i);
        }

        var hits    = texts.Count - uncachedIndices.Count;
        var hitRate = texts.Count > 0 ? (double)hits / texts.Count * 100 : 0d;
        _logger.LogInformation(
            "Embedding cache hit: {Hits}/{Total} ({HitRate:F1}%)",
            hits, texts.Count, hitRate);

        // ── Phase 2: Embed uncached texts in sub-batches of MaxBatchSize ─────
        for (var batchStart = 0; batchStart < uncachedIndices.Count; batchStart += MaxBatchSize)
        {
            var batchSlice   = uncachedIndices.Skip(batchStart).Take(MaxBatchSize).ToList();
            var batchTexts   = batchSlice.Select(i => sanitizedTexts[i]).ToList();

            var (vectors, totalTokens) = await CallEmbeddingApiAsync(batchTexts, cancellationToken);
            var tokensPerItem          = batchTexts.Count > 0 ? totalTokens / batchTexts.Count : 0;

            for (var j = 0; j < batchSlice.Count; j++)
            {
                var origIdx = batchSlice[j];
                results[origIdx] = new EmbeddingResult
                {
                    Embedding  = vectors[j],
                    TokensUsed = tokensPerItem,
                    WasCached  = false,
                };
                await _cache.SetAsync(
                    BuildCacheKey(sanitizedTexts[origIdx]),
                    new CachedEmbedding(vectors[j]),
                    CacheTtl,
                    cancellationToken);
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task IngestDocumentAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentText);

        // ── Step 1: Chunk ─────────────────────────────────────────────────────
        var chunkResult = await _chunking.ChunkDocumentAsync(
            new ChunkingRequest
            {
                DocumentText     = request.DocumentText,
                SourceDocumentId = request.SourceDocumentId,
                SourceName       = request.SourceName,
                Category         = request.Category,
            },
            cancellationToken);

        if (chunkResult.TotalChunks == 0)
        {
            _logger.LogWarning(
                "IngestDocumentAsync: no chunks produced. SourceDocumentId={SourceDocumentId} SourceName={SourceName}",
                request.SourceDocumentId, request.SourceName);
            return;
        }

        // ── Step 2: Embed all chunks in batch ─────────────────────────────────
        var chunkTexts = chunkResult.Chunks.Select(c => c.Content).ToList();
        var embeddings = await GenerateEmbeddingsAsync(chunkTexts, cancellationToken);

        // ── Step 3: Upsert embeddings — partial failure continues (§6) ────────
        var successCount  = 0;
        var cachedCount   = 0;
        var totalApiTokens = 0;

        for (var i = 0; i < chunkResult.Chunks.Count; i++)
        {
            var chunk  = chunkResult.Chunks[i];
            var result = embeddings[i];

            try
            {
                await _vectorSearch.UpsertEmbeddingAsync(
                    request.Category,
                    DeriveChunkId(request.SourceDocumentId, chunk.ChunkIndex),
                    chunk.Content,
                    result.Embedding);

                successCount++;
                if (result.WasCached) cachedCount++;
                else totalApiTokens += result.TokensUsed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "IngestDocumentAsync: upsert failed for chunk {ChunkIndex}. SourceDocumentId={SourceDocumentId}",
                    chunk.ChunkIndex, request.SourceDocumentId);
            }
        }

        _logger.LogInformation(
            "Document {SourceName} ingested: {ChunkCount} chunks, {CachedCount} cached, {ApiTokens} tokens used.",
            request.SourceName, successCount, cachedCount, totalApiTokens);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Removes null bytes to prevent injection into the OpenAI API payload (OWASP A03).
    /// </summary>
    private static string SanitizeText(string text)
        => text.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>
    /// Builds a Redis cache key from the SHA-256 of <paramref name="text"/>.
    /// SHA-256 prevents key collision while keeping the actual text out of the cache key (AIR-S04).
    /// </summary>
    private static string BuildCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"embedding:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Derives a deterministic chunk UUID from <paramref name="sourceDocumentId"/> and
    /// <paramref name="chunkIndex"/> using SHA-256, enabling idempotent upserts on re-ingestion.
    /// </summary>
    private static Guid DeriveChunkId(Guid sourceDocumentId, int chunkIndex)
    {
        Span<byte> input = stackalloc byte[20]; // 16-byte GUID + 4-byte index
        sourceDocumentId.TryWriteBytes(input);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], chunkIndex);
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Calls the OpenAI embeddings API through the named "openai" <see cref="HttpClient"/>
    /// (pre-configured base URL and auth header in Program.cs).
    /// Wrapped in the Polly retry pipeline (AIR-O08).
    /// </summary>
    private async Task<(float[][] Vectors, int TotalTokens)> CallEmbeddingApiAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        return await _retryPipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("openai");

            // OpenAI accepts a string (single) or an array (batch) in the "input" field.
            var requestBody = new
            {
                model      = EmbeddingModel,
                input      = texts.Count == 1 ? (object)texts[0] : texts.ToArray(),
                dimensions = EmbeddingDimensions,
            };

            using var response = await client.PostAsJsonAsync(
                "/v1/embeddings", requestBody, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "EmbeddingGenerationService: OpenAI HTTP {Status}. BatchSize={BatchSize}",
                    (int)response.StatusCode, texts.Count);
                throw new HttpRequestException(
                    $"OpenAI embeddings returned {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: ct);

            return ParseEmbeddingResponse(json, texts.Count);
        }, cancellationToken);
    }

    /// <summary>
    /// Parses the OpenAI embeddings response JSON, validates each vector dimension,
    /// and returns vectors ordered by their original <c>index</c> field.
    /// </summary>
    private (float[][] Vectors, int TotalTokens) ParseEmbeddingResponse(
        JsonElement json,
        int expectedCount)
    {
        var totalTokens = json.TryGetProperty("usage", out var usage)
            ? usage.GetProperty("total_tokens").GetInt32()
            : 0;

        var vectors = new float[expectedCount][];

        foreach (var item in json.GetProperty("data").EnumerateArray())
        {
            var index          = item.GetProperty("index").GetInt32();
            var embeddingArray = item.GetProperty("embedding");
            var vector         = new float[EmbeddingDimensions];
            var j              = 0;

            foreach (var val in embeddingArray.EnumerateArray())
                vector[j++] = val.GetSingle();

            if (j != EmbeddingDimensions)
                throw new InvalidOperationException(
                    $"Expected {EmbeddingDimensions} dimensions, got {j}.");

            vectors[index] = vector;
        }

        return (vectors, totalTokens);
    }

    // ── Private cache DTO ─────────────────────────────────────────────────────

    /// <summary>
    /// Thin JSON-serialisable wrapper for a cached embedding vector.
    /// Using a named record ensures stable JSON property naming across .NET versions.
    /// </summary>
    private sealed record CachedEmbedding(float[] Vector);
}
