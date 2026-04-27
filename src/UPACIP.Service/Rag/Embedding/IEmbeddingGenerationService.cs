using UPACIP.Service.Rag.Embedding.Models;

namespace UPACIP.Service.Rag.Embedding;

/// <summary>
/// Converts document text into 384-dimensional embedding vectors using OpenAI
/// text-embedding-3-small and stores them in pgvector (US_076 AC-2, AC-3, AC-4).
///
/// Three entry-points:
/// <list type="bullet">
///   <item><see cref="GenerateEmbeddingAsync"/> — single text, cache-first.</item>
///   <item><see cref="GenerateEmbeddingsAsync"/> — batch (≤100/call), cache-first with merge.</item>
///   <item><see cref="IngestDocumentAsync"/> — end-to-end: chunk → embed → store.</item>
/// </list>
/// </summary>
public interface IEmbeddingGenerationService
{
    /// <summary>
    /// Generates a 384-dimensional embedding for a single <paramref name="text"/>.
    /// Checks the Redis cache (key: <c>embedding:{sha256(text)}</c>) before calling the API (AIR-O06).
    /// </summary>
    /// <param name="text">Input text. Must not be null or whitespace.</param>
    /// <param name="cancellationToken">Propagates host-shutdown or request cancellation.</param>
    /// <returns>
    /// <see cref="EmbeddingResult"/> with <see cref="EmbeddingResult.WasCached"/> reflecting
    /// whether the API was called.
    /// </returns>
    Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts in a single API call.
    /// Texts already present in the Redis cache are skipped; only uncached texts are sent
    /// to OpenAI in sub-batches of 100 (OpenAI batch limit).
    /// </summary>
    /// <param name="texts">One or more texts to embed. Empty list returns an empty list.</param>
    /// <param name="cancellationToken">Propagates host-shutdown or request cancellation.</param>
    /// <returns>
    /// Results in the same order as <paramref name="texts"/>.
    /// </returns>
    Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// End-to-end document ingestion: chunk the document via
    /// <see cref="Chunking.IDocumentChunkingService"/>, embed all chunks in batch,
    /// and store each embedding via <see cref="VectorSearch.IVectorSearchService.UpsertEmbeddingAsync"/>
    /// (US_076 AC-2, Implementation Plan §6).
    ///
    /// Partial failures (individual chunk upsert errors) are logged and skipped
    /// rather than aborting the entire ingestion.
    /// </summary>
    /// <param name="request">Ingestion parameters (document text, source ID, category).</param>
    /// <param name="cancellationToken">Propagates host-shutdown or request cancellation.</param>
    Task IngestDocumentAsync(IngestionRequest request, CancellationToken cancellationToken = default);
}
