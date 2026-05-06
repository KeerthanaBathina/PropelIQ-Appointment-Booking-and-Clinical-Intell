using Microsoft.Extensions.Options;
using OpenAI.Chat;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Middleware;
using UPACIP.Api.Features.AIGateway.Providers;
using UPACIP.Api.Features.AIGateway.Queue;
using UPACIP.Api.Features.AIGateway.Resilience;
using UPACIP.Api.Features.AIGateway.Services;
using UPACIP.Api.Features.AIGateway.Versioning;
using UPACIP.Service.AiSafety;
using SdkOpenAIAdapter = UPACIP.Api.Features.AIGateway.Providers.OpenAI.OpenAIProviderAdapter;

namespace UPACIP.Api.Features.AIGateway.Extensions;

/// <summary>
/// Extension methods that register all AI Gateway services, middleware pipeline
/// components, and configuration options into the DI container (US_067 AC-1, TASK_002).
///
/// Call <see cref="AddAIGateway"/> from <c>Program.cs</c> after the core services
/// are registered.
/// </summary>
public static class AIGatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers the complete AI Gateway vertical slice into the DI container:
    ///
    /// Configuration options (all validated at startup):
    ///   • <see cref="AIGatewayOptions"/>      — provider priority, token budget overrides.
    ///   • <see cref="OpenAIProviderOptions"/>  — OpenAI model, API key, timeout.
    ///   • <see cref="ClaudeProviderOptions"/>  — Claude model, API key, timeout.
    ///   • <see cref="ResilienceOptions"/>      — circuit-breaker and retry thresholds.
    ///   • <see cref="QueueOptions"/>           — queue keys, concurrency, polling, retry config.
    ///
    /// Provider adapters (Singleton — stateless; HttpClient managed by IHttpClientFactory):
    ///   • <see cref="OpenAIProviderAdapter"/> → registered as <see cref="IAIProviderAdapter"/>.
    ///   • <see cref="ClaudeProviderAdapter"/> → registered as <see cref="IAIProviderAdapter"/>.
    ///
    /// Resilience infrastructure (Singleton — holds per-provider circuit-breaker state):
    ///   • <see cref="AIResiliencePipelineBuilder"/> — Polly V8 pipeline factory.
    ///   • <see cref="AIProviderFallbackHandler"/>   — primary → fallback → error routing.
    ///
    /// Observability (Singleton — thread-safe accumulator):
    ///   • <see cref="ProviderHealthTracker"/> — per-provider metrics and cost tracking.
    ///
    /// Queue infrastructure (AC-4, TR-012, NFR-029):
    ///   • <see cref="IDocumentParsingQueueProducer"/> / <see cref="DocumentParsingQueueProducer"/> — Singleton.
    ///   • <see cref="DeadLetterHandler"/>            — Singleton dead-letter queue handler.
    ///   • <see cref="DocumentParsingQueueConsumer"/>  — Hosted background service (Singleton).
    ///
    /// Middleware pipeline components (Singleton — pure functions, no state):
    ///   • <see cref="AIRequestValidationMiddleware"/>
    ///   • <see cref="AIAuthenticationMiddleware"/>
    ///   • <see cref="AIResponseNormalizationMiddleware"/>
    ///
    /// Core gateway service (Scoped):
    ///   • <see cref="IAIGatewayService"/> / <see cref="AIGatewayService"/>
    /// </summary>
    public static IServiceCollection AddAIGateway(
        this IServiceCollection services,
        IConfiguration           configuration)
    {
        // ── Configuration options ─────────────────────────────────────────────
        services
            .AddOptions<AIGatewayOptions>()
            .Bind(configuration.GetSection(AIGatewayOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<OpenAIProviderOptions>()
            .Bind(configuration.GetSection(OpenAIProviderOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<ClaudeProviderOptions>()
            .Bind(configuration.GetSection(ClaudeProviderOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<QueueOptions>()
            .Bind(configuration.GetSection(QueueOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<TokenBudgetConfiguration>()
            .Bind(configuration.GetSection(TokenBudgetConfiguration.SectionName));

        // ── Token estimation & budget enforcement (US_068, AIR-O01–AIR-O03) ────
        // TokenEstimationService loads cl100k_base BPE vocab once at startup; safe
        // as Singleton since GptEncoding is stateless after construction.
        services.AddSingleton<ITokenEstimationService, TokenEstimationService>();
        // Scoped: shares DI scope with AIGatewayService; stateless between requests.
        services.AddScoped<ITokenBudgetEnforcementService, TokenBudgetEnforcementService>();

        // ── Token budget validator (US_070 TASK_001, AIR-O01–AIR-O03) ────────
        // Strict reject-on-exceed check: returns TokenBudgetResult with IsWithinBudget=false
        // when prompt token count exceeds the configured ceiling. AIGatewayService uses this
        // as Step 3a (before the truncation-based enforcement) to surface deterministic
        // "token budget exceeded" errors to callers.
        services.AddScoped<ITokenBudgetValidator, TokenBudgetValidator>();

        // ── OpenAI .NET SDK ChatClient (Singleton — thread-safe; US_068 TASK_001) ──────
        // Configured with model name and API key from OpenAIProviderOptions.
        // The SDK manages its own internal HttpClient transport.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            return new ChatClient(model: opts.Model, apiKey: opts.ApiKey);
        });

        // ── Claude request builder and response mapper (Singleton — stateless; US_069 TASK_001) ──
        services.AddSingleton<ClaudeRequestBuilder>();
        services.AddSingleton<ClaudeResponseMapper>();

        // ── Model version registry (Singleton — US_069 TASK_003, AIR-O05) ───────────────
        // Must be registered before the provider adapters so they can resolve
        // IModelVersionRegistry in their constructors.
        services.AddSingleton<IModelVersionRegistry, ModelVersionRegistry>();

        // ── Provider adapters (Singleton — stateless, safe for concurrent access) ──
        // SdkOpenAIAdapter (US_068) uses ChatClient; ClaudeProviderAdapter uses raw HttpClient.
        // Registered as IAIProviderAdapter so IEnumerable<IAIProviderAdapter> resolves both.
        services.AddSingleton<IAIProviderAdapter, SdkOpenAIAdapter>();
        services.AddSingleton<IAIProviderAdapter, ClaudeProviderAdapter>();

        // ── Resilience pipeline factory (Transient — stateless builder; pipelines are
        // built once per provider inside AIProviderFallbackHandler) ──────────────
        services.AddTransient<AIResiliencePipelineBuilder>();

        // ── Provider state manager + circuit breaker monitor (Singleton — US_069 TASK_002) ──
        // ProviderStateManager must be registered before AIResiliencePipelineBuilder is
        // resolved so state transitions are captured from the first circuit-breaker event.
        // CircuitBreakerStateMonitor subscribes in its constructor; must resolve after
        // ProviderStateManager to ensure the subscription is wired before any pipelines fire.
        services.AddSingleton<ProviderStateManager>();

        // Redis-backed circuit breaker state store (US_070 TASK_002 EC-2 — cross-instance state).
        // Must be registered before CircuitBreakerStateMonitor (which injects it).
        services.AddSingleton<ICircuitBreakerStateStore, RedisCircuitBreakerStateStore>();

        services.AddSingleton<CircuitBreakerStateMonitor>();

        // Degraded-mode handler: encapsulates dual-provider-failure logic + admin alerting (EC-1).
        // Singleton — no scoped dependencies.
        services.AddSingleton<DegradedModeHandler>();

        // ── Fallback handler (Singleton — holds long-lived Polly circuit-breaker state) ──
        services.AddSingleton<AIProviderFallbackHandler>();

        // ── Provider health and cost tracker (Singleton — thread-safe accumulator) ──
        services.AddSingleton<ProviderHealthTracker>();

        // ── Queue infrastructure (AC-4) ───────────────────────────────────────
        // Producer: Singleton — IConnectionMultiplexer is Singleton; safe for concurrent access.
        services.AddSingleton<IDocumentParsingQueueProducer, DocumentParsingQueueProducer>();

        // Dead-letter handler: Singleton — pushes to Redis LIST; no mutable state.
        services.AddSingleton<DeadLetterHandler>();

        // Consumer: registered as both Singleton and IHostedService so the DI container
        // manages the single instance lifecycle while the host calls StartAsync/StopAsync.
        services.AddSingleton<DocumentParsingQueueConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<DocumentParsingQueueConsumer>());

        // ── Middleware pipeline components (Singleton — pure functions, no state) ──
        services.AddSingleton<AIRequestValidationMiddleware>();
        services.AddSingleton<AIAuthenticationMiddleware>();
        services.AddSingleton<AIResponseNormalizationMiddleware>();

        // ── Cost tracking hooks (US_071 TASK_004) ─────────────────────────────
        // AiRequestCostLogger: Scoped — reads AiCostBudgetConfig (rate card), writes AiRequestLog.
        // AiCostTrackingMiddleware: Singleton — fire-and-forget wrapper; uses IServiceScopeFactory.
        services.AddScoped<IAiRequestCostLogger, AiRequestCostLogger>();
        services.AddSingleton<AiCostTrackingMiddleware>();

        // ── Prompt Injection Sanitization Middleware (US_079 task_001, AIR-S06) ──
        // Singleton — delegates to IPromptInjectionDetector (also Singleton); placed BEFORE
        // PiiRedactionMiddleware in the gateway pipeline so injection detection operates on
        // raw user text before PII tokens are substituted.
        // IPromptInjectionDetector is registered in Program.cs before AddAIGateway().
        services.AddSingleton<PromptSanitizationMiddleware>();

        // ── PII Redaction Middleware (US_074 task_001, AC-3, AIR-S01) ─────────
        // Singleton — delegates to IPiiRedactionService (also Singleton); wraps the
        // gateway-facing redact/log methods and creates a sanitised AIRequest copy.
        // IPiiRedactionService is registered in Program.cs before AddAIGateway().
        services.AddSingleton<PiiRedactionMiddleware>();

        // ── Core gateway service (Scoped) ─────────────────────────────────────
        services.AddScoped<IAIGatewayService, AIGatewayService>();

        return services;
    }
}
