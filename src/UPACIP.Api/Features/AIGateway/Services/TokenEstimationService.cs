using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpToken;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// SharpToken-backed <see cref="ITokenEstimationService"/> for the GPT-4o-mini model
/// (cl100k_base BPE encoding) (US_068 TASK_002, AIR-O07).
///
/// Thread-safety: <see cref="GptEncoding"/> is stateless after construction; this class
/// is safe to register as a Singleton and called concurrently.
///
/// Sentence-boundary truncation strategy (AIR-O01–O03 edge case):
/// <list type="number">
///   <item>Encode the full prompt to BPE token IDs.</item>
///   <item>If token count &lt;= maxTokens, return the original string unchanged.</item>
///   <item>Decode the first maxTokens tokens back to a UTF-8 string.</item>
///   <item>
///     Search the last 10 % of the decoded string for a sentence boundary
///     (period, exclamation, question mark followed by whitespace or end-of-string).
///   </item>
///   <item>
///     If a boundary is found, truncate at that point and trim trailing whitespace.
///     Otherwise return the decoded exact-token string as-is.
///   </item>
/// </list>
/// </summary>
public sealed class TokenEstimationService : ITokenEstimationService
{
    // Sentence-boundary split: match end of sentence punctuation followed by whitespace or EOS.
    private static readonly Regex SentenceBoundaryRegex =
        new(@"(?<=[.!?])(\s+|$)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly GptEncoding                  _encoding;
    private readonly ILogger<TokenEstimationService> _logger;

    public TokenEstimationService(ILogger<TokenEstimationService> logger)
    {
        // GetEncodingForModel resolves "gpt-4o-mini" → cl100k_base vocabulary.
        _encoding = GptEncoding.GetEncodingForModel("gpt-4o-mini");
        _logger   = logger;
    }

    /// <inheritdoc/>
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return _encoding.Encode(text).Count;
    }

    /// <inheritdoc/>
    public string TruncateToTokenLimit(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text) || maxTokens <= 0)
            return string.Empty;

        var tokens = _encoding.Encode(text);

        if (tokens.Count <= maxTokens)
            return text; // Already within budget — no allocation needed.

        // Decode the first maxTokens BPE token IDs back to a string.
        var truncatedTokens = tokens.Take(maxTokens).ToList();
        var decoded         = _encoding.Decode(truncatedTokens);

        // Search the last 10 % of the decoded string for a sentence boundary.
        int searchStart = (int)(decoded.Length * 0.90);
        var searchWindow = decoded[searchStart..];

        var matches = SentenceBoundaryRegex.Matches(searchWindow);
        if (matches.Count > 0)
        {
            // Use the last boundary found within the window.
            var lastMatch   = matches[^1];
            int cutPosition = searchStart + lastMatch.Index + lastMatch.Length - lastMatch.Groups[1].Length;
            var result      = decoded[..cutPosition].TrimEnd();

            _logger.LogDebug(
                "TokenEstimationService: sentence-boundary truncation applied. " +
                "OriginalTokens={OriginalTokens} MaxTokens={MaxTokens} " +
                "DecodedLength={DecodedLength} CutPosition={CutPosition}",
                tokens.Count, maxTokens, decoded.Length, cutPosition);

            return result;
        }

        // No sentence boundary found — return exact-token truncation.
        _logger.LogDebug(
            "TokenEstimationService: exact-token truncation applied (no sentence boundary). " +
            "OriginalTokens={OriginalTokens} MaxTokens={MaxTokens}",
            tokens.Count, maxTokens);

        return decoded;
    }
}
