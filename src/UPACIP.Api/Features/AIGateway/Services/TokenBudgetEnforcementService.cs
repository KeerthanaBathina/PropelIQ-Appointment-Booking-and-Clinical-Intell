using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Enforces per-request-type token budgets (US_068 TASK_002, AIR-O01, AIR-O02, AIR-O03).
///
/// Budget resolution order:
/// <list type="number">
///   <item>
///     Check <see cref="TokenBudgetConfiguration.RequestTypeBudgets"/> (appsettings-configurable
///     overrides) by the <see cref="AIRequestType"/> name string.
///   </item>
///   <item>
///     Fall back to the hard-coded <see cref="TokenBudget.For(AIRequestType)"/> presets when
///     the configuration section is absent or the key is missing.
///   </item>
/// </list>
///
/// When the estimated prompt token count exceeds the input ceiling, the prompt is truncated
/// at the last sentence boundary within the last 10 % of the decoded text (or exactly at the
/// token boundary if no sentence boundary is found). The truncation event is captured in
/// <see cref="TruncationMetadata"/> and emitted as a Serilog structured log entry.
///
/// Registered as <c>Scoped</c> — shares the per-HTTP-request DI scope with
/// <see cref="AIGatewayService"/>.
/// </summary>
public sealed class TokenBudgetEnforcementService : ITokenBudgetEnforcementService
{
    private readonly ITokenEstimationService             _tokenEstimation;
    private readonly IOptions<TokenBudgetConfiguration>  _budgetConfig;
    private readonly ILogger<TokenBudgetEnforcementService> _logger;

    public TokenBudgetEnforcementService(
        ITokenEstimationService                  tokenEstimation,
        IOptions<TokenBudgetConfiguration>        budgetConfig,
        ILogger<TokenBudgetEnforcementService>    logger)
    {
        _tokenEstimation = tokenEstimation;
        _budgetConfig    = budgetConfig;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public Task<(AIRequest EnforcedRequest, TruncationMetadata? Metadata)> EnforceAsync(
        AIRequest         request,
        CancellationToken cancellationToken = default)
    {
        // ── Resolve effective budget ──────────────────────────────────────────
        var (maxInput, maxOutput) = ResolveBudget(request.RequestType);

        // ── Count input tokens ────────────────────────────────────────────────
        int originalTokenCount = _tokenEstimation.EstimateTokenCount(request.Prompt);

        TruncationMetadata? truncationMetadata = null;
        string enforcedPrompt = request.Prompt;

        // ── Truncate if over budget ────────────────────────────────────────────
        if (originalTokenCount > maxInput)
        {
            enforcedPrompt = _tokenEstimation.TruncateToTokenLimit(request.Prompt, maxInput);
            int truncatedCount = _tokenEstimation.EstimateTokenCount(enforcedPrompt);

            truncationMetadata = new TruncationMetadata(
                OriginalTokenCount:  originalTokenCount,
                TruncatedTokenCount: truncatedCount,
                MaxAllowedTokens:    maxInput,
                RequestType:         request.RequestType);

            // Structured log: no PII — key names only, no values (AIR-S01).
            _logger.LogWarning(
                "TokenBudget: prompt truncated. " +
                "CorrelationId={CorrelationId} RequestType={RequestType} " +
                "OriginalTokenCount={OriginalTokenCount} TruncatedTokenCount={TruncatedTokenCount} " +
                "EnforcedBudget={EnforcedBudget} WasTruncated={WasTruncated}",
                request.CorrelationId,
                request.RequestType,
                originalTokenCount,
                truncatedCount,
                maxInput,
                true);
        }
        else
        {
            // Informational: log even when not truncated so budget compliance is auditable.
            _logger.LogInformation(
                "TokenBudget: within limits. " +
                "CorrelationId={CorrelationId} RequestType={RequestType} " +
                "OriginalTokenCount={OriginalTokenCount} EnforcedBudget={EnforcedBudget} WasTruncated={WasTruncated}",
                request.CorrelationId,
                request.RequestType,
                originalTokenCount,
                maxInput,
                false);
        }

        // ── Build enforced request ─────────────────────────────────────────────
        // AIRequest uses init-only properties; construct a fresh instance copying
        // all fields and applying the budget-clamped prompt + output ceiling.
        var enforcedRequest = new AIRequest
        {
            RequestId      = request.RequestId,
            RequestType    = request.RequestType,
            Prompt         = enforcedPrompt,
            SystemMessage  = request.SystemMessage,
            MaxInputTokens = maxInput,
            MaxOutputTokens = maxOutput,
            Temperature    = request.Temperature,
            Metadata       = request.Metadata,
            CorrelationId  = request.CorrelationId,
        };

        return Task.FromResult<(AIRequest, TruncationMetadata?)>((enforcedRequest, truncationMetadata));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective (maxInput, maxOutput) pair for the given request type.
    /// Config overrides take precedence over <see cref="TokenBudget"/> hard-coded presets.
    /// </summary>
    private (int MaxInput, int MaxOutput) ResolveBudget(AIRequestType requestType)
    {
        var budgets = _budgetConfig.Value.RequestTypeBudgets;
        string key  = requestType.ToString();

        if (budgets.TryGetValue(key, out var configLimit) &&
            configLimit.MaxInputTokens > 0 &&
            configLimit.MaxOutputTokens > 0)
        {
            return (configLimit.MaxInputTokens, configLimit.MaxOutputTokens);
        }

        // Fall back to hard-coded presets (TokenBudget.For throws on unknown type).
        var preset = TokenBudget.For(requestType);
        return (preset.MaxInputTokens, preset.MaxOutputTokens);
    }
}
