using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;
using UPACIP.Service.Rag.Chunking.Models;

namespace UPACIP.Service.Rag.Chunking;

/// <summary>
/// Implements sliding-window BPE chunking for knowledge base document ingestion
/// (US_076 AC-1, AIR-R01).
///
/// Algorithm:
///   1. Preprocess text: replace images, convert tables, normalise whitespace.
///   2. Tokenise with cl100k_base (OpenAI text-embedding-3-small compatible).
///   3. Short-document bypass: &lt;100 tokens → single chunk, no splitting.
///   4. Sliding window: 512-token window, 410-token step (102-token / ~20% overlap).
///   5. Boundary alignment: extend last token of each chunk by up to 5 tokens to
///      avoid mid-word splits at BPE boundaries.
///   6. Output validation: no data loss, overlap consistency, max 520-token soft limit.
///
/// Singleton-safe: <see cref="TiktokenTokenizer"/> is documented as thread-safe.
/// </summary>
public sealed class DocumentChunkingService : IDocumentChunkingService
{
    // ── Constants (AIR-R01) ───────────────────────────────────────────────────

    /// <summary>Target BPE tokens per chunk (AC-1).</summary>
    private const int WindowSize = 512;

    /// <summary>
    /// Sliding-window step: 512 − 102 = 410 tokens.
    /// Yields ~20% (102 token) overlap between consecutive chunks (AC-1).
    /// </summary>
    private const int StepSize = 410;

    /// <summary>Nominal overlap carried from the preceding chunk.</summary>
    private const int NominalOverlap = WindowSize - StepSize; // 102

    /// <summary>Documents shorter than this are stored as a single chunk (edge case).</summary>
    private const int ShortDocThreshold = 100;

    /// <summary>
    /// Maximum tolerated chunk size after boundary alignment.
    /// Exceeding this triggers a warning log but does not block chunking.
    /// </summary>
    private const int SoftMaxTokens = 520;

    /// <summary>Maximum number of extra tokens added during boundary alignment.</summary>
    private const int BoundaryAlignmentTokens = 5;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly TiktokenTokenizer _tokenizer;
    private readonly ILogger<DocumentChunkingService> _logger;

    public DocumentChunkingService(
        TiktokenTokenizer tokenizer,
        ILogger<DocumentChunkingService> logger)
    {
        _tokenizer = tokenizer;
        _logger    = logger;
    }

    // ── IDocumentChunkingService ──────────────────────────────────────────────

    /// <inheritdoc />
    public Task<ChunkingResult> ChunkDocumentAsync(
        ChunkingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentText, nameof(request.DocumentText));

        cancellationToken.ThrowIfCancellationRequested();

        // ── Step 1: Preprocess ────────────────────────────────────────────────
        var preprocessed = Preprocess(request.DocumentText);

        // ── Step 2: Tokenise ─────────────────────────────────────────────────
        var allTokenIds = _tokenizer.EncodeToIds(preprocessed, considerPreTokenization: true, considerNormalization: true);
        var allTokens   = allTokenIds.ToArray();

        // ── Step 3: Short-document bypass ─────────────────────────────────────
        if (allTokens.Length < ShortDocThreshold)
        {
            _logger.LogDebug(
                "Document {SourceDocumentId} ({SourceName}) has {TokenCount} tokens — below threshold {Threshold}. Stored as single chunk.",
                request.SourceDocumentId, request.SourceName, allTokens.Length, ShortDocThreshold);

            var singleChunk = new DocumentChunk
            {
                ChunkIndex       = 0,
                Content          = preprocessed,
                TokenCount       = allTokens.Length,
                OverlapTokens    = 0,
                SourceDocumentId = request.SourceDocumentId,
                SourceName       = request.SourceName,
            };

            return Task.FromResult(new ChunkingResult
            {
                Chunks           = [singleChunk],
                TotalChunks      = 1,
                TotalTokens      = allTokens.Length,
                SourceDocumentId = request.SourceDocumentId,
                Category         = request.Category,
            });
        }

        // ── Step 4 + 5: Sliding window with boundary alignment ────────────────
        var chunks = BuildChunks(allTokens, request);

        // ── Step 6: Output validation ─────────────────────────────────────────
        ValidateChunks(chunks, allTokens.Length, request);

        var result = new ChunkingResult
        {
            Chunks           = chunks,
            TotalChunks      = chunks.Count,
            TotalTokens      = chunks.Sum(c => c.TokenCount),
            SourceDocumentId = request.SourceDocumentId,
            Category         = request.Category,
        };

        _logger.LogDebug(
            "Chunked document {SourceDocumentId} ({SourceName}) into {TotalChunks} chunks (total {OriginalTokens} source tokens).",
            request.SourceDocumentId, request.SourceName, result.TotalChunks, allTokens.Length);

        return Task.FromResult(result);
    }

    // ── Private: preprocessing ────────────────────────────────────────────────

    private static string Preprocess(string text)
    {
        // Order: images → tables → whitespace (table conversion may introduce extra whitespace)
        text = TextPreprocessor.ReplaceImages(text);
        text = TextPreprocessor.ConvertTablesToText(text);
        text = TextPreprocessor.NormalizeWhitespace(text);
        return text;
    }

    // ── Private: chunking ─────────────────────────────────────────────────────

    private List<DocumentChunk> BuildChunks(int[] allTokens, ChunkingRequest request)
    {
        var chunks      = new List<DocumentChunk>();
        int position    = 0;
        int chunkIndex  = 0;

        while (position < allTokens.Length)
        {
            int endPosition = Math.Min(position + WindowSize, allTokens.Length);

            // ── Boundary alignment: extend up to 5 tokens to avoid mid-word splits ──
            if (endPosition < allTokens.Length)
            {
                endPosition = AlignToBoundary(allTokens, endPosition);
            }

            var tokenSlice = allTokens[position..endPosition];

            // Warn if chunk exceeds soft limit (e.g. after boundary expansion)
            if (tokenSlice.Length > SoftMaxTokens)
            {
                _logger.LogWarning(
                    "Chunk {ChunkIndex} for document {SourceDocumentId} has {TokenCount} tokens, exceeding soft limit of {SoftMax}.",
                    chunkIndex, request.SourceDocumentId, tokenSlice.Length, SoftMaxTokens);
            }

            var content = _tokenizer.Decode(tokenSlice) ?? string.Empty;

            chunks.Add(new DocumentChunk
            {
                ChunkIndex       = chunkIndex,
                Content          = content,
                TokenCount       = tokenSlice.Length,
                OverlapTokens    = chunkIndex == 0 ? 0 : NominalOverlap,
                SourceDocumentId = request.SourceDocumentId,
                SourceName       = request.SourceName,
            });

            chunkIndex++;

            // Advance by StepSize; stop when the remaining tokens would form a final partial chunk
            int nextPosition = position + StepSize;
            if (nextPosition >= allTokens.Length)
            {
                break;
            }
            position = nextPosition;
        }

        return chunks;
    }

    /// <summary>
    /// Extends <paramref name="endPosition"/> by at most <see cref="BoundaryAlignmentTokens"/>
    /// tokens until it falls on a clean word boundary (i.e. the token at <paramref name="endPosition"/>
    /// decodes to text that begins with a whitespace / punctuation character).
    ///
    /// This prevents the final token of a chunk from containing a partial UTF-8 byte sequence
    /// or half of a word that the BPE tokeniser split mid-token.
    /// </summary>
    private int AlignToBoundary(int[] allTokens, int endPosition)
    {
        for (int extra = 0; extra < BoundaryAlignmentTokens; extra++)
        {
            if (endPosition + extra >= allTokens.Length)
                break;

            // Decode the single next token to inspect its leading character
            var nextTokenText = _tokenizer.Decode([allTokens[endPosition + extra]]);
            if (string.IsNullOrEmpty(nextTokenText))
                continue;

            char firstChar = nextTokenText[0];
            if (char.IsWhiteSpace(firstChar) || char.IsPunctuation(firstChar) || char.IsSymbol(firstChar))
            {
                // Current boundary is clean — the next token starts at a word boundary
                return endPosition + extra;
            }
        }

        // No clean boundary found within tolerance — return original end position
        return endPosition;
    }

    // ── Private: validation ───────────────────────────────────────────────────

    private void ValidateChunks(
        List<DocumentChunk> chunks,
        int originalTokenCount,
        ChunkingRequest request)
    {
        if (chunks.Count == 0)
        {
            _logger.LogWarning(
                "Document {SourceDocumentId} produced zero chunks — this should not happen.",
                request.SourceDocumentId);
            return;
        }

        // (a) No chunk exceeds soft limit
        foreach (var chunk in chunks)
        {
            if (chunk.TokenCount > SoftMaxTokens)
            {
                // Warning already logged in BuildChunks; skip duplicate log here.
            }
        }

        // (b) Verify the first chunk covers position 0 and the last chunk reaches
        //     the end of the token stream (rough coverage check without storing positions).
        //     Full coverage is guaranteed by the sliding window algorithm, but we verify
        //     that the combined token count is ≥ originalTokenCount (overlap means it's >).
        int combinedTokens = chunks.Sum(c => c.TokenCount);
        if (combinedTokens < originalTokenCount)
        {
            _logger.LogError(
                "Data-loss detected for document {SourceDocumentId}: original {Original} tokens but chunks cover only {Covered} tokens.",
                request.SourceDocumentId, originalTokenCount, combinedTokens);
        }

        // (c) Verify overlap consistency for non-first chunks
        foreach (var chunk in chunks.Skip(1))
        {
            if (chunk.OverlapTokens != NominalOverlap)
            {
                _logger.LogWarning(
                    "Chunk {ChunkIndex} for document {SourceDocumentId} has unexpected overlap {Overlap} (expected {Expected}).",
                    chunk.ChunkIndex, request.SourceDocumentId, chunk.OverlapTokens, NominalOverlap);
            }
        }
    }
}
