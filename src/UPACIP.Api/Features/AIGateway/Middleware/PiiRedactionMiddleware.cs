using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Service.AiSafety;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Singleton AI Gateway middleware component that applies PII redaction on outbound
/// AI requests and associates inbound AI responses with patients via internal reference
/// IDs only (US_074 task_001, AC-3, AC-4, AIR-S01).
///
/// <para>Pipeline position (inside <c>AIGatewayService.ProcessAsync</c>):</para>
/// <list type="number">
///   <item>Auth check</item>
///   <item>Validation</item>
///   <item>Token budget enforcement</item>
///   <item><b>PII redaction ← this middleware</b></item>
///   <item>Queue routing / provider dispatch</item>
///   <item>Response normalization</item>
///   <item>Cost tracking</item>
/// </list>
///
/// <para>
/// On outbound: calls <see cref="IPiiRedactionService.RedactPii"/> to strip all six PII
/// categories, then calls <see cref="IPiiRedactionService.ContainsPii"/> as a validation
/// gate.  If residual PII is detected the request is blocked and a critical-level log event
/// is emitted without logging the PII content (AIR-S01).
/// </para>
///
/// <para>
/// On inbound: AI responses are stored and correlated with patients via the internal
/// <c>PatientId</c> GUID — no PII placeholder restoration is applied to stored results
/// per AC-4.  A structured redaction event (token counts per category, no PII values) is
/// logged for audit.
/// </para>
///
/// <para>
/// Singleton lifetime: <see cref="IPiiRedactionService"/> is also Singleton (stateless
/// regex operations), so no scope management is required.
/// </para>
/// </summary>
public sealed class PiiRedactionMiddleware
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IPiiRedactionService              _redactionService;
    private readonly ILogger<PiiRedactionMiddleware>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public PiiRedactionMiddleware(
        IPiiRedactionService            redactionService,
        ILogger<PiiRedactionMiddleware> logger)
    {
        _redactionService = redactionService;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redacts PII from the prompt in <paramref name="request"/> and returns the
    /// sanitised request plus the per-request <see cref="PiiRedactionContext"/>.
    ///
    /// <para>
    /// Extracts optional patient context from <c>request.Metadata</c>:
    /// <list type="bullet">
    ///   <item><c>patientId</c>   — Guid (required for AC-4 re-association context).</item>
    ///   <item><c>patientName</c> — full name for name-pattern redaction (optional).</item>
    ///   <item><c>patientDob</c>  — date string for exact DOB redaction (optional).</item>
    ///   <item><c>patientPhone</c>— phone string for exact phone redaction (optional).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Returns <c>isBlocked = true</c> when the post-redaction validation gate
    /// (<see cref="IPiiRedactionService.ContainsPii"/>) detects residual PII.
    /// In that case the caller MUST NOT dispatch the request to the AI provider.
    /// </para>
    /// </summary>
    /// <returns>
    /// Tuple of (sanitised <see cref="AIRequest"/>, <see cref="PiiRedactionContext"/>,
    /// isBlocked flag).
    /// </returns>
    public (AIRequest sanitisedRequest, PiiRedactionContext context, bool isBlocked) RedactRequest(
        AIRequest request)
    {
        // ── Extract patient context from metadata ─────────────────────────────
        Guid patientId = Guid.TryParse(
            request.Metadata.GetValueOrDefault("patientId"), out var pid)
            ? pid
            : Guid.Empty;

        request.Metadata.TryGetValue("patientName",  out string? patientName);
        request.Metadata.TryGetValue("patientDob",   out string? patientDob);
        request.Metadata.TryGetValue("patientPhone", out string? patientPhone);

        // ── Redact the prompt ─────────────────────────────────────────────────
        var (redactedPrompt, ctx) = _redactionService.RedactPii(
            request.Prompt,
            patientId,
            patientName,
            patientDob,
            patientPhone);

        // Also redact the system message if present.
        string? redactedSystemMessage = request.SystemMessage;
        if (!string.IsNullOrEmpty(request.SystemMessage))
        {
            (redactedSystemMessage, _) = _redactionService.RedactPii(
                request.SystemMessage,
                patientId,
                patientName,
                patientDob,
                patientPhone);
        }

        // ── Build sanitised request ───────────────────────────────────────────
        var sanitised = CloneWithRedactedContent(request, redactedPrompt, redactedSystemMessage);

        // ── Post-redaction validation gate (AIR-S01) ──────────────────────────
        bool isBlocked = _redactionService.ContainsPii(redactedPrompt) ||
                         (!string.IsNullOrEmpty(redactedSystemMessage) &&
                          _redactionService.ContainsPii(redactedSystemMessage));

        if (isBlocked)
        {
            // Critical log — does NOT include actual PII (only CorrelationId and RequestId).
            _logger.LogCritical(
                "PiiRedaction: Residual PII detected after redaction pass. " +
                "Request BLOCKED. CorrelationId={CorrelationId} RequestId={RequestId} " +
                "RequestType={RequestType}.",
                request.CorrelationId, request.RequestId, request.RequestType);
        }

        return (sanitised, ctx, isBlocked);
    }

    /// <summary>
    /// Logs a structured redaction audit event recording token counts per PII category.
    /// No actual PII values are written to the log (AIR-S01).
    /// </summary>
    /// <param name="ctx">Redaction context from the outbound pass.</param>
    /// <param name="correlationId">AI Gateway correlation identifier.</param>
    public void LogRedactionEvent(PiiRedactionContext ctx, string correlationId)
    {
        if (!ctx.HasRedactions)
        {
            _logger.LogDebug(
                "PiiRedaction: No PII tokens found in prompt. CorrelationId={CorrelationId}.",
                correlationId);
            return;
        }

        // Build a safe summary string: "SSN=1, EMAIL=1" — no PII values.
        string summary = string.Join(", ",
            ctx.RedactedCounts.Select(kv => $"{kv.Key}={kv.Value}"));

        _logger.LogInformation(
            "PiiRedaction: {TotalTokens} PII token(s) redacted. " +
            "Categories=[{Summary}] PatientId={PatientId} CorrelationId={CorrelationId}.",
            ctx.Mappings.Count, summary, ctx.PatientId, correlationId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a copy of <paramref name="original"/> with the prompt and system message
    /// replaced by their redacted equivalents.  All other fields are preserved.
    /// </summary>
    private static AIRequest CloneWithRedactedContent(
        AIRequest original,
        string    redactedPrompt,
        string?   redactedSystemMessage)
    {
        return new AIRequest
        {
            RequestId       = original.RequestId,
            RequestType     = original.RequestType,
            Prompt          = redactedPrompt,
            SystemMessage   = redactedSystemMessage,
            MaxInputTokens  = original.MaxInputTokens,
            MaxOutputTokens = original.MaxOutputTokens,
            Temperature     = original.Temperature,
            Metadata        = original.Metadata,
            CorrelationId   = original.CorrelationId,
        };
    }
}
