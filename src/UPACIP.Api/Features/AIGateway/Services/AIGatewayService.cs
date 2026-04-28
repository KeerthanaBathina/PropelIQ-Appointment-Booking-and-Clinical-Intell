using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Middleware;
using UPACIP.Api.Features.AIGateway.Models;
using UPACIP.Api.Features.AIGateway.Queue;
using UPACIP.Api.Features.AIGateway.Resilience;
using UPACIP.Service.AI;
using UPACIP.Service.Performance;
using UPACIP.Service.Performance.Models;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Core AI Gateway orchestration service (US_067 AC-1, AC-2, AC-3, AC-4, AIR-O01–AIR-O03).
///
/// Pipeline per request:
///   1. Authentication  — Staff/Admin role check via <see cref="AIAuthenticationMiddleware"/>.
///   2. Validation      — Schema + token-budget check via <see cref="AIRequestValidationMiddleware"/>.
///   3. Token budget    — Accurate token counting + input truncation via
///      <see cref="ITokenBudgetEnforcementService"/> (US_068, AIR-O01–O03).
///      Pre-validation (<see cref="ITokenBudgetValidator"/>, US_070) rejects requests
///      that exceed the configured ceiling before truncation is attempted.
///   4. Queue routing   — <see cref="AIRequestType.DocumentParsing"/> requests are enqueued via
///      <see cref="IDocumentParsingQueueProducer"/> and return a 202-style queued response (AC-4).
///      All other request types proceed to synchronous provider dispatch.
///   5. Provider dispatch — Delegates to <see cref="AIProviderFallbackHandler"/> which applies
///      per-provider Polly V8 resilience pipelines and routes primary (OpenAI) → fallback
///      (Claude) without caller awareness (AC-2).
///   6. Normalization   — Passes raw adapter response through <see cref="AIResponseNormalizationMiddleware"/>.
///   7. Audit logging   — Records full request/response metadata via <see cref="AiAuditLogger"/>
///      (PII redaction is applied by the logger itself — AIR-S01).
/// </summary>
public sealed class AIGatewayService : IAIGatewayService
{
    private readonly AIGatewayOptions                    _options;
    private readonly AiOperationTimeoutsOptions          _timeouts;
    private readonly AIProviderFallbackHandler           _fallbackHandler;
    private readonly IDocumentParsingQueueProducer       _queueProducer;
    private readonly ITokenBudgetEnforcementService      _budgetEnforcement;
    private readonly ITokenBudgetValidator               _budgetValidator;
    private readonly AIRequestValidationMiddleware       _validation;
    private readonly AIAuthenticationMiddleware          _authentication;
    private readonly AIResponseNormalizationMiddleware   _normalization;
    private readonly AiCostTrackingMiddleware            _costTracking;
    private readonly AiAuditLogger                       _auditLogger;
    private readonly PiiRedactionMiddleware              _piiRedaction;
    private readonly AiAuditLoggingMiddleware            _auditLogging;
    private readonly IPriorityRequestQueue               _priorityQueue;
    private readonly ILogger<AIGatewayService>           _logger;

    public AIGatewayService(
        IOptions<AIGatewayOptions>          options,
        IOptions<AiOperationTimeoutsOptions> timeouts,
        AIProviderFallbackHandler           fallbackHandler,
        IDocumentParsingQueueProducer       queueProducer,
        ITokenBudgetEnforcementService      budgetEnforcement,
        ITokenBudgetValidator               budgetValidator,
        AIRequestValidationMiddleware       validation,
        AIAuthenticationMiddleware          authentication,
        AIResponseNormalizationMiddleware   normalization,
        AiCostTrackingMiddleware            costTracking,
        AiAuditLogger                       auditLogger,
        PiiRedactionMiddleware              piiRedaction,
        AiAuditLoggingMiddleware            auditLogging,
        IPriorityRequestQueue               priorityQueue,
        ILogger<AIGatewayService>           logger)
    {
        _options           = options.Value;
        _timeouts          = timeouts.Value;
        _fallbackHandler   = fallbackHandler;
        _queueProducer     = queueProducer;
        _budgetEnforcement = budgetEnforcement;
        _budgetValidator   = budgetValidator;
        _validation        = validation;
        _authentication    = authentication;
        _normalization     = normalization;
        _costTracking      = costTracking;
        _auditLogger       = auditLogger;
        _piiRedaction      = piiRedaction;
        _auditLogging      = auditLogging;
        _priorityQueue     = priorityQueue;
        _logger            = logger;
    }

    /// <inheritdoc/>
    public async Task<AIResponse> ProcessAsync(
        AIRequest        request,
        ClaimsPrincipal  caller,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: Authentication ────────────────────────────────────────────
        var authResult = _authentication.Authorize(caller);
        if (!authResult.IsAuthorized)
        {
            _logger.LogWarning(
                "AI Gateway: unauthorized request. CorrelationId={CorrelationId} Reason={Reason}",
                request.CorrelationId, authResult.ErrorMessage);

            return AIResponse.Failed(request.RequestId, authResult.ErrorMessage!, sw.ElapsedMilliseconds);
        }

        // ── Step 2: Validation ────────────────────────────────────────────────
        var validationResult = _validation.Validate(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "AI Gateway: invalid request. CorrelationId={CorrelationId} " +
                "RequestType={RequestType} Reason={Reason}",
                request.CorrelationId, request.RequestType, validationResult.ErrorMessage);

            return AIResponse.Failed(request.RequestId, validationResult.ErrorMessage!, sw.ElapsedMilliseconds);
        }

        // ── Step 3: Token budget enforcement (US_068, AIR-O01–AIR-O03) ────────
        // Step 3a: Validate — reject requests that exceed the budget ceiling (US_070 AC-1).
        // This strict gate fires BEFORE truncation so that callers receive a deterministic
        // error response rather than a silently-shortened prompt.
        var budgetValidation = await _budgetValidator.ValidateAsync(request, cancellationToken);
        if (!budgetValidation.IsWithinBudget)
        {
            _logger.LogWarning(
                "AI Gateway: request rejected — token budget exceeded. " +
                "CorrelationId={CorrelationId} RequestType={RequestType} " +
                "ActualInputTokens={ActualInputTokens} InputLimit={InputLimit}",
                request.CorrelationId,
                request.RequestType,
                budgetValidation.ActualInputTokenCount,
                budgetValidation.InputTokenLimit);

            return AIResponse.Failed(
                request.RequestId,
                $"Token budget exceeded for '{request.RequestType}': prompt contains " +
                $"{budgetValidation.ActualInputTokenCount} tokens but the limit is " +
                $"{budgetValidation.InputTokenLimit} input tokens.",
                sw.ElapsedMilliseconds);
        }

        // Step 3b: Enforce — count prompt tokens accurately via SharpToken (cl100k_base),
        // truncate oversized prompts at sentence boundaries, and pin the output ceiling.
        // Runs as a defense-in-depth layer after the validator confirms the request
        // is within budget (handles edge cases from configuration drift).
        var (enforcedRequest, truncationMetadata) =
            await _budgetEnforcement.EnforceAsync(request, cancellationToken);

        // Work with the budget-enforced request from this point forward.
        request = enforcedRequest;

        // ── Step 3b: Effective token limits (used for budget-aware logging) ───
        _ = _options.GetEffectiveBudget(request.RequestType);

        // ── Step 3c: PII redaction (AIR-S01, US_074 AC-3, AC-4) ─────────────
        // All six PII categories are stripped from the prompt and system message
        // before any further processing (queue or direct dispatch).
        var (sanitisedRequest, piiCtx, isBlocked) = _piiRedaction.RedactRequest(request);

        if (isBlocked)
        {
            return AIResponse.Failed(
                request.RequestId,
                "Request blocked: residual PII detected after redaction pass.",
                sw.ElapsedMilliseconds);
        }

        // Use the sanitised request for all downstream pipeline steps.
        request = sanitisedRequest;

        // ── Step 4a: Queue routing for DocumentParsing requests (AC-4) ───────
        // DocumentParsing jobs are offloaded to Redis queue for asynchronous processing
        // to prevent AI provider rate limit violations (AIR-O07, TR-012, NFR-029).
        // Caller receives a queued-acknowledgement response immediately (202 semantics).
        if (request.RequestType == AIRequestType.DocumentParsing)
        {
            var documentId = request.Metadata.TryGetValue("documentId", out var docIdStr) &&
                             Guid.TryParse(docIdStr, out var parsedDocId)
                ? parsedDocId
                : Guid.Empty;

            var receipt = await _queueProducer.EnqueueAsync(
                request, documentId, request.CorrelationId, cancellationToken);

            _logger.LogInformation(
                "AI Gateway: DocumentParsing request queued for async processing. " +
                "CorrelationId={CorrelationId} JobId={JobId} QueueDepth={Depth}",
                request.CorrelationId, receipt.JobId, receipt.QueueDepth);

            return AIResponse.Queued(request.RequestId, receipt.JobId, sw.ElapsedMilliseconds);
        }

        // ── Step 4b: Audit log outgoing request (PII redaction in AiAuditLogger) ──
        _auditLogger.LogRequest(
            correlationId: Guid.TryParse(request.CorrelationId, out var cid) ? cid : Guid.NewGuid(),
            operation:     request.RequestType.ToString(),
            provider:      "gateway",
            model:         "routing",
            promptContent: request.Prompt);

        // ── Step 4 cont.: Provider dispatch via priority queue + per-operation timeout ──
        // Route to the in-process priority queue so concurrent AI calls are throttled:
        //   MedicalCoding   → Normal  (max 10 concurrent)
        //   ConversationalIntake → Normal
        //   All others      → Normal (default)
        // Per-operation timeout is enforced via a linked CancellationTokenSource.
        // The fallback Polly pipeline runs inside the timeout budget (AC-2, AC-3).
        var priority = request.RequestType switch
        {
            AIRequestType.MedicalCoding       => RequestPriority.Normal,
            AIRequestType.ConversationalIntake => RequestPriority.Normal,
            _                                  => RequestPriority.Normal,
        };

        var timeoutMs = request.RequestType switch
        {
            AIRequestType.DocumentParsing      => _timeouts.DocumentParsing,
            AIRequestType.MedicalCoding        => _timeouts.MedicalCoding,
            AIRequestType.ConversationalIntake => _timeouts.ConversationalIntake,
            _                                  => _timeouts.Default,
        };

        AIResponse rawResponse;
        try
        {
            rawResponse = await _priorityQueue.ExecuteAsync(
                async ct =>
                {
                    // Create a per-operation timeout token linked to the caller's token.
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

                    return await _fallbackHandler.ExecuteAsync(request, timeoutCts.Token);
                },
                priority,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-operation timeout fired before caller cancelled.
            _logger.LogWarning(
                "AI Gateway: per-operation timeout fired. " +
                "RequestType={RequestType} TimeoutMs={TimeoutMs} CorrelationId={CorrelationId}",
                request.RequestType, timeoutMs, request.CorrelationId);

            return AIResponse.Failed(
                request.RequestId,
                $"{request.RequestType} request timed out after {timeoutMs}ms.",
                sw.ElapsedMilliseconds);
        }

        // Configuration-alert: model version mismatch detection on the returned response.
        if (!string.IsNullOrEmpty(rawResponse.ProviderName) &&
            _options.ExpectedModelVersions.TryGetValue(rawResponse.ProviderName, out var expectedVersion) &&
            !string.IsNullOrEmpty(rawResponse.ModelVersion) &&
            !string.Equals(rawResponse.ModelVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AI Gateway: model version mismatch for provider '{Provider}'. " +
                "Expected={Expected} Actual={Actual} CorrelationId={CorrelationId}",
                rawResponse.ProviderName, expectedVersion, rawResponse.ModelVersion,
                request.CorrelationId);
        }

        // Audit log the response.
        _auditLogger.LogResponse(
            correlationId: Guid.TryParse(request.CorrelationId, out var rCid) ? rCid : Guid.NewGuid(),
            operation:     request.RequestType.ToString(),
            provider:      rawResponse.ProviderName,
            model:         rawResponse.ModelVersion,
            success:       rawResponse.Success,
            latencyMs:     rawResponse.LatencyMs,
            inputTokens:   rawResponse.InputTokensUsed,
            outputTokens:  rawResponse.OutputTokensUsed);

        // ── Step 5: Normalization ─────────────────────────────────────────────
        var normalized = _normalization.Normalize(rawResponse);
        // Attach truncation metadata when input was trimmed before dispatch.
        if (truncationMetadata is not null)
            normalized = normalized.WithTruncation(truncationMetadata);
        // ── Step 6: Structured lifecycle log ─────────────────────────────────
        _logger.LogInformation(
            "AI Gateway: request completed. CorrelationId={CorrelationId} " +
            "RequestType={RequestType} Provider={Provider} Success={Success} " +
            "LatencyMs={LatencyMs} InputTokens={InputTokens} OutputTokens={OutputTokens}",
            request.CorrelationId,
            request.RequestType,
            normalized.ProviderName,
            normalized.Success,
            normalized.LatencyMs,
            normalized.InputTokensUsed,
            normalized.OutputTokensUsed);

        // ── Step 7: Cost tracking (US_071 TASK_004) ───────────────────────────
        // Fire-and-forget — never blocks or fails the AI response pipeline.
        // Logs AiRequestLog and checks near-real-time budget threshold.
        _costTracking.Track(request, normalized);

        // ── Step 7b: PII redaction audit event (US_074 AC-3, AIR-S01) ─────────
        // Logs only token counts per category — no PII values are written.
        _piiRedaction.LogRedactionEvent(piiCtx, request.CorrelationId);

        // ── Step 8: Database audit logging (US_080 task_002, AIR-S04, AC-3) ───
        // Captures post-PII-redacted prompt, response, tokens, latency, and A/B
        // metadata into the ai_audit_logs partitioned table via bounded channel.
        // Fire-and-forget — never blocks or delays the AI response.
        var userId = caller.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                     ?? "unknown";
        _auditLogging.LogInteraction(request, normalized, userId);

        return normalized;
    }
}
