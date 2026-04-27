using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Contracts;

/// <summary>
/// Unified response contract returned by all AI operations through the AI Gateway.
/// Strips provider-specific internals so consumers only depend on this contract (AC-3).
///
/// On failure <see cref="Success"/> is <see langword="false"/> and
/// <see cref="ErrorMessage"/> contains a user-safe description.
/// The raw provider error is logged server-side only.
/// </summary>
public sealed class AIResponse
{
    /// <summary>
    /// Mirrors <see cref="AIRequest.RequestId"/> for correlation across logs and retries.
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>The generated text content from the model.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Short identifier of the provider that fulfilled the request (e.g., "openai").</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>Model version string as reported by the provider (e.g., "gpt-4o-mini").</summary>
    public string ModelVersion { get; init; } = string.Empty;

    /// <summary>Number of input tokens consumed as counted by the provider.</summary>
    public int InputTokensUsed { get; init; }

    /// <summary>Number of output tokens generated as counted by the provider.</summary>
    public int OutputTokensUsed { get; init; }

    /// <summary>
    /// Optional confidence score in [0, 1] returned by providers that surface it.
    /// Defaults to 0 when the provider does not report confidence.
    /// </summary>
    public float ConfidenceScore { get; init; }

    /// <summary>End-to-end latency in milliseconds for the provider round-trip.</summary>
    public long LatencyMs { get; init; }

    /// <summary><see langword="true"/> when the provider returned a usable response.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// User-safe error description when <see cref="Success"/> is <see langword="false"/>.
    /// Never contains raw exception messages, stack traces, or API error payloads (AC-3).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// <see langword="true"/> when the request has been accepted into the async queue
    /// rather than processed synchronously (AC-4 — DocumentParsing via Redis queue).
    /// Callers should surface HTTP 202 Accepted when this is true.
    /// </summary>
    public bool IsQueued { get; init; }

    /// <summary>
    /// The queue job identifier assigned by the Redis producer.
    /// Populated only when <see cref="IsQueued"/> is <see langword="true"/>.
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>
    /// <see langword="true"/> when the input prompt was truncated to fit within the
    /// per-request-type token budget before being forwarded to the provider
    /// (US_068 TASK_002, AIR-O01–AIR-O03 edge case).
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Populated when <see cref="Truncated"/> is <see langword="true"/>.
    /// Contains the original token count, the truncated token count, and the
    /// budget limit that triggered truncation.
    /// </summary>
    public TruncationMetadata? TruncationInfo { get; init; }

    // ── Convenience factories ─────────────────────────────────────────────────

    /// <summary>Creates a successful response from provider output.</summary>
    public static AIResponse Succeeded(
        Guid requestId,
        string content,
        string providerName,
        string modelVersion,
        int inputTokensUsed,
        int outputTokensUsed,
        float confidenceScore,
        long latencyMs) => new()
    {
        RequestId        = requestId,
        Content          = content,
        ProviderName     = providerName,
        ModelVersion     = modelVersion,
        InputTokensUsed  = inputTokensUsed,
        OutputTokensUsed = outputTokensUsed,
        ConfidenceScore  = confidenceScore,
        LatencyMs        = latencyMs,
        Success          = true,
    };

    /// <summary>
    /// Returns a copy of this response with truncation metadata applied.
    /// Used by <c>AIGatewayService</c> after the provider returns to attach
    /// the truncation event that occurred before dispatch.
    /// </summary>
    public AIResponse WithTruncation(TruncationMetadata metadata) => new()
    {
        RequestId        = RequestId,
        Content          = Content,
        ProviderName     = ProviderName,
        ModelVersion     = ModelVersion,
        InputTokensUsed  = InputTokensUsed,
        OutputTokensUsed = OutputTokensUsed,
        ConfidenceScore  = ConfidenceScore,
        LatencyMs        = LatencyMs,
        Success          = Success,
        ErrorMessage     = ErrorMessage,
        IsQueued         = IsQueued,
        JobId            = JobId,
        Truncated        = true,
        TruncationInfo   = metadata,
    };

    /// <summary>Creates a failed response with a user-safe error message.</summary>
    public static AIResponse Failed(Guid requestId, string errorMessage, long latencyMs = 0) => new()
    {
        RequestId    = requestId,
        Success      = false,
        ErrorMessage = errorMessage,
        LatencyMs    = latencyMs,
    };

    /// <summary>
    /// Creates a queued acknowledgement response returned when a
    /// <see cref="AIRequestType.DocumentParsing"/> request is accepted into the async
    /// Redis queue (AC-4). Callers should surface HTTP 202 Accepted.
    /// </summary>
    public static AIResponse Queued(Guid requestId, Guid jobId, long latencyMs = 0) => new()
    {
        RequestId = requestId,
        JobId     = jobId,
        Success   = true,
        IsQueued  = true,
        LatencyMs = latencyMs,
        Content   = $"Request accepted and queued for processing. JobId: {jobId}",
    };
}
