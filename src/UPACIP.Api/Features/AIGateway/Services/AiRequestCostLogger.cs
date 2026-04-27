using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Scoped implementation of <see cref="IAiRequestCostLogger"/> that maps the normalized
/// <see cref="AIResponse"/> token counts to an <c>AiRequestLog</c> row using the rate card
/// stored in <c>AiCostBudgetConfig</c> (US_071 TASK_004, AC-1).
///
/// <para>
/// Provider name mapping:
/// <list type="bullet">
///   <item><c>"openai"</c> → <see cref="AiProvider.OpenAI"/></item>
///   <item><c>"anthropic"</c> → <see cref="AiProvider.Anthropic"/></item>
/// </list>
/// </para>
///
/// <para>
/// Request type mapping: <see cref="AIRequestType"/> and <see cref="AiRequestType"/> share
/// identical integer values (DocumentParsing=1, ConversationalIntake=2, MedicalCoding=3),
/// allowing a safe direct cast between the two enums.
/// </para>
///
/// <para>
/// Cost source: always <see cref="AiCostSource.Approximate"/> because neither OpenAI nor
/// Anthropic return dollar-denominated cost in their API responses; cost is derived from the
/// configured rate card (<c>inputTokens × CostPer1kInputTokens / 1000 + outputTokens × CostPer1kOutputTokens / 1000</c>).
/// </para>
///
/// <para>
/// Scoped lifetime — requires <see cref="ApplicationDbContext"/> for reading
/// <c>AiCostBudgetConfig</c> (rate card) and writing <c>AiRequestLog</c>.
/// </para>
/// </summary>
public sealed class AiRequestCostLogger : IAiRequestCostLogger
{
    // ─────────────────────────────────────────────────────────────────────────
    // Provider name → AiProvider enum mapping (case-insensitive).
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

    private readonly ApplicationDbContext           _db;
    private readonly ILogger<AiRequestCostLogger>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiRequestCostLogger(
        ApplicationDbContext         db,
        ILogger<AiRequestCostLogger> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiRequestCostLogger
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task LogAsync(AIRequest request, AIResponse response, CancellationToken ct = default)
    {
        // Skip queued responses — the actual AI call has not been made yet.
        if (response.IsQueued) return;

        // Skip if we cannot identify the provider — no cost data can be recorded.
        if (!ProviderMap.TryGetValue(response.ProviderName ?? string.Empty, out var provider))
        {
            _logger.LogWarning(
                "AiRequestCostLogger: unknown provider '{ProviderName}', skipping cost log. " +
                "CorrelationId={CorrelationId}",
                response.ProviderName, request.CorrelationId);
            return;
        }

        // Skip if token counts are both zero — indicates no actual provider round-trip
        // (e.g., pre-dispatch validation failure that propagated here erroneously).
        if (response.InputTokensUsed == 0 && response.OutputTokensUsed == 0)
        {
            _logger.LogDebug(
                "AiRequestCostLogger: skipping log — zero token counts. " +
                "Provider={Provider} CorrelationId={CorrelationId}",
                provider, request.CorrelationId);
            return;
        }

        // Map AIRequestType (API layer) → AiRequestType (DataAccess layer).
        // The two enums share identical integer values by design (documented on AiRequestType).
        var requestType = (AiRequestType)(int)request.RequestType;

        // Load the rate card for cost calculation (cost source is always Approximate
        // because neither OpenAI nor Anthropic return dollar cost in API responses).
        var rateCard = await _db.AiCostBudgetConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == provider, ct);

        decimal estimatedCost;
        if (rateCard is not null)
        {
            estimatedCost =
                (response.InputTokensUsed  * rateCard.CostPer1kInputTokens  / 1_000m) +
                (response.OutputTokensUsed * rateCard.CostPer1kOutputTokens / 1_000m);
        }
        else
        {
            // Rate card not found — log a warning and record 0 cost so token data is preserved.
            _logger.LogWarning(
                "AiRequestCostLogger: no rate card found for provider {Provider}. " +
                "Recording zero estimated cost. CorrelationId={CorrelationId}",
                provider, request.CorrelationId);
            estimatedCost = 0m;
        }

        // Parse correlation ID — fall back to a new GUID if malformed.
        var correlationId = Guid.TryParse(request.CorrelationId, out var parsedCid)
            ? parsedCid
            : Guid.NewGuid();

        _db.AiRequestLogs.Add(new AiRequestLog
        {
            Provider        = provider,
            RequestType     = requestType,
            InputTokens     = response.InputTokensUsed,
            OutputTokens    = response.OutputTokensUsed,
            EstimatedCost   = estimatedCost,
            CostSource      = AiCostSource.Approximate, // Always rate-card derived (see class summary)
            CorrelationId   = correlationId,
            CreatedAt       = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "AiRequestCostLogger: logged cost. Provider={Provider} RequestType={RequestType} " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens} EstimatedCost={Cost:F6} " +
            "CorrelationId={CorrelationId}",
            provider, requestType,
            response.InputTokensUsed, response.OutputTokensUsed,
            estimatedCost, request.CorrelationId);
    }
}
