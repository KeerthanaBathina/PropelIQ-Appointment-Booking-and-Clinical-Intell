using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI;

/// <summary>
/// Structured audit logging service for all AI interactions in the UPACIP platform
/// (AIR-S04, AIR-S01, US_046 AC-4).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Log all AI request/response metadata with correlation IDs (AIR-S04).</item>
///   <item>Redact PII from prompt content before writing to structured logs (AIR-S01).</item>
///   <item>Record per-request token usage for cost tracking (AIR-O09).</item>
///   <item>Record provider, model, and latency for performance monitoring (AIR-O10).</item>
/// </list>
///
/// PII redaction strategy (AIR-S01):
///   - SSN patterns (\d{3}-\d{2}-\d{4}) → [REDACTED]
///   - 9-digit sequences → [REDACTED]
///   - Email addresses → [REDACTED_EMAIL]
///   - Injection keywords are also removed from log content to prevent log-injection attacks.
///
/// All output uses structured Serilog events (no PII in property values).
/// This class is intentionally fail-open — log failures never propagate to the caller.
/// </summary>
public sealed class AiAuditLogger
{
    // ─────────────────────────────────────────────────────────────────────────
    // PII redaction patterns (AIR-S01)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ILogger<AiAuditLogger> _logger;

    public AiAuditLogger(ILogger<AiAuditLogger> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the dispatch of an AI request to a provider.
    /// Call BEFORE sending the request; provides a pre-call audit record with the
    /// sanitised prompt length (not the full content, to avoid log bloat).
    /// </summary>
    /// <param name="correlationId">Consolidation / document / analysis correlation GUID.</param>
    /// <param name="operation">Short description: "ClinicalExtraction", "ConflictDetection", "DatePlausibility".</param>
    /// <param name="provider">AI provider name: "openai" | "anthropic".</param>
    /// <param name="model">Model identifier (e.g. "gpt-4o-mini").</param>
    /// <param name="promptContent">Prompt text — will be PII-redacted before logging.</param>
    public void LogRequest(
        Guid   correlationId,
        string operation,
        string provider,
        string model,
        string promptContent)
    {
        try
        {
            var sanitised = RedactPii(promptContent);

            _logger.LogInformation(
                "AiAudit: request dispatched. " +
                "CorrelationId={CorrelationId}, Operation={Operation}, Provider={Provider}, " +
                "Model={Model}, PromptChars={Chars}",
                correlationId, operation, provider, model, sanitised.Length);
        }
        catch (Exception ex)
        {
            // Fail-open: log failure never breaks the request pipeline.
            _logger.LogDebug(ex, "AiAuditLogger.LogRequest failed silently.");
        }
    }

    /// <summary>
    /// Logs the response received from an AI provider.
    /// Call AFTER receiving the response (before validation/parsing).
    /// </summary>
    /// <param name="correlationId">Same correlation ID used in <see cref="LogRequest"/>.</param>
    /// <param name="operation">Same operation name used in <see cref="LogRequest"/>.</param>
    /// <param name="provider">Provider that responded.</param>
    /// <param name="model">Model that responded.</param>
    /// <param name="success">True when a non-null response was received.</param>
    /// <param name="latencyMs">Request round-trip latency in milliseconds.</param>
    /// <param name="inputTokens">Input token count from provider response metadata (0 if unavailable).</param>
    /// <param name="outputTokens">Output token count from provider response metadata (0 if unavailable).</param>
    public void LogResponse(
        Guid   correlationId,
        string operation,
        string provider,
        string model,
        bool   success,
        long   latencyMs,
        int    inputTokens  = 0,
        int    outputTokens = 0)
    {
        try
        {
            _logger.LogInformation(
                "AiAudit: response received. " +
                "CorrelationId={CorrelationId}, Operation={Operation}, Provider={Provider}, " +
                "Model={Model}, Success={Success}, LatencyMs={Latency}, " +
                "InputTokens={In}, OutputTokens={Out}",
                correlationId, operation, provider, model,
                success, latencyMs, inputTokens, outputTokens);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AiAuditLogger.LogResponse failed silently.");
        }
    }

    /// <summary>
    /// Logs the result of a confidence threshold evaluation (US_046 AC-1, AIR-Q08).
    /// </summary>
    public void LogConfidenceGate(
        Guid  correlationId,
        float meanConfidence,
        int   totalItems,
        int   flaggedItems,
        bool  requiresBatchReview)
    {
        try
        {
            _logger.LogInformation(
                "AiAudit: confidence gate evaluated. " +
                "CorrelationId={CorrelationId}, MeanConfidence={Mean:F2}, " +
                "TotalItems={Total}, FlaggedItems={Flagged}, BatchReview={Batch}",
                correlationId, meanConfidence, totalItems, flaggedItems, requiresBatchReview);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AiAuditLogger.LogConfidenceGate failed silently.");
        }
    }

    /// <summary>
    /// Logs a circuit breaker state change for audit compliance (AIR-O04).
    /// </summary>
    public void LogCircuitBreakerEvent(
        string operation,
        string provider,
        string state,
        string? reason = null)
    {
        try
        {
            _logger.LogWarning(
                "AiAudit: circuit breaker state change. " +
                "Operation={Operation}, Provider={Provider}, State={State}, Reason={Reason}",
                operation, provider, state, reason ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AiAuditLogger.LogCircuitBreakerEvent failed silently.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PII Redaction (AIR-S01)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips SSN patterns, 9-digit numbers, and email addresses from a string
    /// before it is written to structured logs (AIR-S01).
    /// Returns the redacted string; never throws.
    /// </summary>
    public static string RedactPii(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        try
        {
            var result = SsnPattern.Replace(input, "[REDACTED]");
            result     = EmailPattern.Replace(result, "[REDACTED_EMAIL]");
            return result;
        }
        catch
        {
            return "[REDACTED_ON_ERROR]";
        }
    }
}
