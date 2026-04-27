namespace UPACIP.Api.Features.AIGateway.Contracts;

/// <summary>
/// Abstraction over a single LLM provider (e.g., OpenAI, Anthropic).
/// All provider adapters implement this interface so the AI Gateway can switch
/// between them transparently during Polly circuit-breaker failover (AC-1, AIR-O04).
///
/// Provider adapters are registered as named services; the gateway resolves the
/// correct adapter by name via the DI container.
/// </summary>
public interface IAIProviderAdapter
{
    /// <summary>
    /// Short lower-case identifier for the provider (e.g., <c>"openai"</c>, <c>"anthropic"</c>).
    /// Unique within the application; used as DI registration key.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// The provider's model version string in use (e.g., <c>"gpt-4o-mini"</c>).
    /// Logged per-request for observability; compared against expected version from config
    /// to emit a configuration-alert warning when they diverge (edge-case: API version change).
    /// </summary>
    string ModelVersion { get; }

    /// <summary>
    /// Sends a completion request to the provider and returns the normalized response.
    /// Implementations are responsible for HTTP transport only — Polly resilience wrapping
    /// is applied by the calling <see cref="UPACIP.Api.Features.AIGateway.Services.IAIGatewayService"/>.
    /// </summary>
    /// <param name="request">The unified request contract.</param>
    /// <param name="cancellationToken">Propagated from the originating HTTP request.</param>
    /// <returns>
    /// A populated <see cref="AIResponse"/> with <see cref="AIResponse.Success"/> set
    /// to <see langword="true"/> on success, or <see langword="false"/> on provider error.
    /// Implementations must NOT throw on provider errors — catch and return a failed response.
    /// </returns>
    Task<AIResponse> SendCompletionAsync(AIRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Probes the provider for connectivity / availability.
    /// Used by the gateway's health check and circuit-breaker pre-flight logic.
    /// </summary>
    /// <returns><see langword="true"/> if the provider is reachable and accepting requests.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
