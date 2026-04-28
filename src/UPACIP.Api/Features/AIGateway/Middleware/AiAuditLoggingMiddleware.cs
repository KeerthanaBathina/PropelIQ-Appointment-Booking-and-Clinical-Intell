using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Service.AiAudit;
using UPACIP.Service.AiAudit.Models;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Singleton AI Gateway middleware that captures the completed AI request/response
/// interaction and enqueues it for asynchronous persistence to the <c>ai_audit_logs</c>
/// partitioned table (US_080 task_002, AIR-S04, AC-3).
///
/// <para>
/// <b>Pipeline position:</b> Called immediately after cost tracking (last step of the
/// synchronous AI Gateway pipeline) before the response is returned to the controller.
/// </para>
///
/// <para>
/// <b>PII safety:</b> The middleware reads <see cref="AIRequest.Prompt"/> AFTER
/// <c>PiiRedactionMiddleware</c> has already stripped all six PII categories.
/// The redacted request is the one forwarded throughout the gateway pipeline,
/// so any prompt text arriving here is guaranteed to be PII-free (AIR-S01).
/// </para>
///
/// <para>
/// <b>Fire-and-forget:</b> Delegates to <see cref="IAiAuditService.LogAiInteractionAsync"/>
/// which enqueues to a bounded channel and returns immediately.
/// If the channel is full, the entry is dropped (logged as warning) — AI responses
/// are never blocked or delayed by audit persistence (NFR-030).
/// </para>
///
/// <para>
/// <b>A/B experiment correlation:</b> Extracts experiment ID and variant from
/// <see cref="AIRequest.Metadata"/> keys set by <c>AbTestingMiddleware</c>:
/// <c>AbExperimentId</c> and <c>AbVariant</c>.
/// </para>
///
/// <para>Singleton lifetime — depends only on <see cref="IAiAuditService"/> (Singleton).</para>
/// </summary>
public sealed class AiAuditLoggingMiddleware
{
    private const string MetadataKeyPatientId      = "patientId";
    private const string MetadataKeyAbExperimentId = "AbExperimentId";
    private const string MetadataKeyAbVariant      = "AbVariant";

    private readonly IAiAuditService                      _auditService;
    private readonly ILogger<AiAuditLoggingMiddleware>    _logger;

    public AiAuditLoggingMiddleware(
        IAiAuditService                    auditService,
        ILogger<AiAuditLoggingMiddleware>  logger)
    {
        _auditService = auditService;
        _logger       = logger;
    }

    /// <summary>
    /// Extracts audit data from the completed request/response pair and enqueues
    /// a persistence record. Returns synchronously after the channel write.
    /// </summary>
    /// <param name="request">AI Gateway request (post-PII-redaction).</param>
    /// <param name="response">Normalized AI Gateway response.</param>
    /// <param name="userId">Authenticated user identifier (ClaimTypes.NameIdentifier).</param>
    public void LogInteraction(AIRequest request, AIResponse response, string userId)
    {
        // Skip queued responses — no synchronous AI interaction occurred.
        if (response.IsQueued) return;

        // Skip failed responses without content — nothing to audit.
        if (!response.Success && string.IsNullOrWhiteSpace(response.Content)) return;

        try
        {
            // ── Extract optional patient ID from request metadata ─────────────
            Guid? patientId = null;
            if (request.Metadata.TryGetValue(MetadataKeyPatientId, out var pidStr)
                && Guid.TryParse(pidStr, out var parsedPid))
            {
                patientId = parsedPid;
            }

            // ── Extract optional A/B experiment metadata ──────────────────────
            Guid? abExperimentId = null;
            if (request.Metadata.TryGetValue(MetadataKeyAbExperimentId, out var abExpStr)
                && Guid.TryParse(abExpStr, out var parsedExp))
            {
                abExperimentId = parsedExp;
            }

            string? abVariant = request.Metadata.TryGetValue(MetadataKeyAbVariant, out var variant)
                ? variant
                : null;

            // ── Confidence: 0 from the gateway means "not reported" ───────────
            float? confidenceScore = response.ConfidenceScore > 0f
                ? response.ConfidenceScore
                : null;

            var entry = new AiAuditLogEntry
            {
                Id              = Guid.NewGuid(),
                Prompt          = request.Prompt,           // Post-PII-redaction — safe to persist.
                Response        = response.Content,
                ModelVersion    = response.ModelVersion,
                InputTokens     = response.InputTokensUsed,
                OutputTokens    = response.OutputTokensUsed,
                LatencyMs       = response.LatencyMs,
                ConfidenceScore = confidenceScore,
                RequestType     = request.RequestType.ToString(),
                PatientId       = patientId,
                AbExperimentId  = abExperimentId,
                AbVariant       = abVariant,
                UserId          = userId,
                CreatedAt       = DateTime.UtcNow,
            };

            // Fire-and-forget — channel write returns ValueTask.CompletedTask in the happy path.
            _ = _auditService.LogAiInteractionAsync(entry, CancellationToken.None)
                             .AsTask();
        }
        catch (Exception ex)
        {
            // Fail-open — never propagate audit logging failures to the AI response pipeline.
            _logger.LogError(ex,
                "AiAuditLoggingMiddleware: failed to enqueue audit entry. " +
                "CorrelationId={CorrelationId} RequestType={RequestType}",
                request.CorrelationId, request.RequestType);
        }
    }
}
