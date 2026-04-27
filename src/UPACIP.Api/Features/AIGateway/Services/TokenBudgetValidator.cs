using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// <see cref="ITokenBudgetValidator"/> implementation that counts input tokens using the
/// SharpToken cl100k_base BPE tokenizer and rejects requests that exceed the configured
/// budget ceiling (US_070 TASK_001, AIR-O01, AIR-O02, AIR-O03).
///
/// <para><strong>Budget resolution order</strong> (same as <c>TokenBudgetEnforcementService</c>):
/// <list type="number">
///   <item>Per-type override in <see cref="TokenBudgetConfiguration.RequestTypeBudgets"/>
///   (from <c>appsettings.json</c>).</item>
///   <item>Hard-coded <see cref="TokenBudget.For(AIRequestType)"/> presets.</item>
/// </list>
/// </para>
///
/// <para>Registered as <c>Scoped</c> — participates in the same DI scope as
/// <see cref="AIGatewayService"/>.</para>
/// </summary>
public sealed class TokenBudgetValidator : ITokenBudgetValidator
{
    private readonly ITokenEstimationService              _tokenEstimation;
    private readonly IOptions<TokenBudgetConfiguration>   _budgetConfig;
    private readonly ILogger<TokenBudgetValidator>        _logger;

    public TokenBudgetValidator(
        ITokenEstimationService               tokenEstimation,
        IOptions<TokenBudgetConfiguration>    budgetConfig,
        ILogger<TokenBudgetValidator>         logger)
    {
        _tokenEstimation = tokenEstimation;
        _budgetConfig    = budgetConfig;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public Task<TokenBudgetResult> ValidateAsync(
        AIRequest         request,
        CancellationToken cancellationToken = default)
    {
        var (maxInput, maxOutput) = ResolveBudget(request.RequestType);

        // Count tokens using the SharpToken cl100k_base BPE encoder
        // (same encoding used by GPT-4o-mini; close enough for Claude 3.5 Sonnet).
        int actualInputTokens = _tokenEstimation.EstimateTokenCount(request.Prompt);

        bool isWithinBudget = actualInputTokens <= maxInput;

        if (isWithinBudget)
        {
            // Structured log: PASS — auditable without any PII from the prompt (AIR-S01).
            _logger.LogInformation(
                "TokenBudgetValidator: PASS. " +
                "CorrelationId={CorrelationId} RequestType={RequestType} " +
                "ActualInputTokens={ActualInputTokens} InputLimit={InputLimit} OutputLimit={OutputLimit}",
                request.CorrelationId,
                request.RequestType,
                actualInputTokens,
                maxInput,
                maxOutput);
        }
        else
        {
            // Structured log: FAIL — no prompt content logged (PII guard, AIR-S01).
            _logger.LogWarning(
                "TokenBudgetValidator: FAIL — request exceeds input token budget. " +
                "CorrelationId={CorrelationId} RequestType={RequestType} " +
                "ActualInputTokens={ActualInputTokens} InputLimit={InputLimit} " +
                "Overage={Overage}",
                request.CorrelationId,
                request.RequestType,
                actualInputTokens,
                maxInput,
                actualInputTokens - maxInput);
        }

        var result = new TokenBudgetResult(
            IsWithinBudget:       isWithinBudget,
            RequestType:          request.RequestType,
            ActualInputTokenCount: actualInputTokens,
            InputTokenLimit:      maxInput,
            OutputTokenLimit:     maxOutput);

        return Task.FromResult(result);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective (maxInput, maxOutput) for the given request type.
    /// Configuration overrides take precedence over hard-coded <see cref="TokenBudget"/> presets.
    /// </summary>
    private (int MaxInput, int MaxOutput) ResolveBudget(AIRequestType requestType)
    {
        var budgets = _budgetConfig.Value.RequestTypeBudgets;
        string key  = requestType.ToString();

        if (budgets.TryGetValue(key, out var configLimit) &&
            configLimit.MaxInputTokens  > 0 &&
            configLimit.MaxOutputTokens > 0)
        {
            return (configLimit.MaxInputTokens, configLimit.MaxOutputTokens);
        }

        var preset = TokenBudget.For(requestType);
        return (preset.MaxInputTokens, preset.MaxOutputTokens);
    }
}
