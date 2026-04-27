using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Services;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.AiCost;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Singleton non-blocking middleware that fires after each AI provider response to:
/// <list type="number">
///   <item>
///     Persist an <c>AiRequestLog</c> row via <see cref="IAiRequestCostLogger"/>
///     (US_071 TASK_004, AC-1).
///   </item>
///   <item>
///     Perform a near-real-time budget threshold check by reading the running daily
///     cost from <see cref="IAiCostAggregationService.GetRunningDailyCostAsync"/> and
///     comparing it against <c>AiCostBudgetConfig.DailyBudgetThreshold</c>.  When the
///     threshold is breached and <c>AlertEnabled = true</c> a <c>LogCritical</c> event
///     is emitted (US_071 TASK_004, AC-2).
///   </item>
/// </list>
///
/// <para>
/// Non-blocking contract: <see cref="Track"/> is fire-and-forget.  Cost tracking failures
/// are caught and logged as warnings so the AI response pipeline is NEVER blocked or failed
/// due to cost logging errors (US_071 TASK_004 implementation plan step 5).
/// </para>
///
/// <para>
/// Singleton lifetime: uses <see cref="IServiceScopeFactory"/> to create a fresh DI scope
/// per fire-and-forget task, ensuring scoped services (<see cref="IAiRequestCostLogger"/>,
/// <see cref="IAiCostAggregationService"/>, <see cref="ApplicationDbContext"/>) are properly
/// isolated and disposed after each background run.
/// </para>
/// </summary>
public sealed class AiCostTrackingMiddleware
{
    // ─────────────────────────────────────────────────────────────────────────
    // Provider name → AiProvider enum (mirrors AiRequestCostLogger mapping).
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, AiProvider> ProviderMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"]    = AiProvider.OpenAI,
            ["anthropic"] = AiProvider.Anthropic,
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory                _scopeFactory;
    private readonly ILogger<AiCostTrackingMiddleware>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiCostTrackingMiddleware(
        IServiceScopeFactory               scopeFactory,
        ILogger<AiCostTrackingMiddleware>  logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues cost logging and threshold checking as a fire-and-forget background task.
    /// Returns immediately — the AI response pipeline is never blocked.
    /// </summary>
    public void Track(AIRequest request, AIResponse response)
    {
        // Skip queued and responses without provider data — no AI round-trip occurred.
        if (response.IsQueued) return;
        if (string.IsNullOrEmpty(response.ProviderName)) return;

        // Capture values needed inside the closure to avoid capturing the original objects.
        var capturedRequest     = request;
        var capturedResponse    = response;
        var capturedCorrelation = request.CorrelationId;

        // Fire-and-forget: do not await, do not capture the Task reference
        // (exception handling is inside the async lambda).
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                // ── Step 1: Log cost entry ─────────────────────────────────────
                var costLogger = scope.ServiceProvider.GetRequiredService<IAiRequestCostLogger>();
                await costLogger.LogAsync(capturedRequest, capturedResponse);

                // ── Step 2: Near-real-time threshold check ─────────────────────
                if (ProviderMap.TryGetValue(capturedResponse.ProviderName, out var provider))
                {
                    await CheckThresholdAsync(scope.ServiceProvider, provider, capturedCorrelation);
                }
            }
            catch (Exception ex)
            {
                // Cost tracking must NEVER propagate exceptions — log and swallow.
                _logger.LogWarning(ex,
                    "AiCostTrackingMiddleware: cost tracking failed. " +
                    "CorrelationId={CorrelationId}. AI response was already returned to caller.",
                    capturedCorrelation);
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the running daily cost for <paramref name="provider"/> and emits a
    /// <c>LogCritical</c> event when budget is exceeded and <c>AlertEnabled = true</c>.
    /// </summary>
    private async Task CheckThresholdAsync(
        IServiceProvider serviceProvider,
        AiProvider       provider,
        string           correlationId)
    {
        try
        {
            var aggregationService = serviceProvider.GetRequiredService<IAiCostAggregationService>();
            var runningCost        = await aggregationService.GetRunningDailyCostAsync(provider);

            // Load budget config for this provider's threshold and alert toggle.
            var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var config = await db.AiCostBudgetConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Provider == provider);

            if (config is null || !config.AlertEnabled) return;

            if (runningCost > config.DailyBudgetThreshold)
            {
                var percentageOver = config.DailyBudgetThreshold > 0m
                    ? Math.Round((runningCost - config.DailyBudgetThreshold) / config.DailyBudgetThreshold * 100m, 2)
                    : 0m;

                _logger.LogCritical(
                    "AI_COST_REALTIME_BREACH — Provider={Provider} running daily cost has exceeded " +
                    "the budget threshold. RunningCost={RunningCost:F6} USD " +
                    "Threshold={Threshold:F6} USD OverBudget={PercentageOver:F2}% " +
                    "CorrelationId={CorrelationId}",
                    provider, runningCost, config.DailyBudgetThreshold,
                    percentageOver, correlationId);
            }
        }
        catch (Exception ex)
        {
            // Redis or DB unavailable for threshold check — log warning and continue.
            _logger.LogWarning(ex,
                "AiCostTrackingMiddleware: threshold check failed for provider {Provider}. " +
                "Skipping. CorrelationId={CorrelationId}",
                provider, correlationId);
        }
    }
}
