namespace UPACIP.Api.Features.AIGateway.Versioning;

/// <summary>
/// Thread-safe in-memory registry that tracks active and previous model versions for each
/// AI provider and executes admin-triggered rollbacks (US_069 TASK_003, AIR-O05).
///
/// <para>Provider adapters resolve their runtime model identifier from
/// <see cref="GetActiveVersion"/> rather than directly from <c>IOptions&lt;T&gt;</c>,
/// so a rollback takes effect on the next outgoing request without service restart.</para>
///
/// <para>On application startup the registry is seeded from
/// <c>IOptionsMonitor&lt;OpenAIProviderOptions&gt;.CurrentValue.Model</c> and
/// <c>IOptionsMonitor&lt;ClaudeProviderOptions&gt;.CurrentValue.Model</c>.
/// <c>OnChange</c> callbacks keep the registry in sync if the underlying
/// configuration file is updated on disk (e.g., hot-patched in a Docker environment).</para>
/// </summary>
public interface IModelVersionRegistry
{
    /// <summary>
    /// Returns the currently active model identifier for <paramref name="providerName"/>.
    /// Defaults to the startup value if no rollback has been performed.
    /// </summary>
    string GetActiveVersion(string providerName);

    /// <summary>
    /// Returns the previous model identifier for <paramref name="providerName"/>
    /// (the rollback target), or <see langword="null"/> if only one version has been recorded.
    /// </summary>
    string? GetPreviousVersion(string providerName);

    /// <summary>
    /// Returns an immutable read-only snapshot of all tracked providers and their
    /// current version state. Used by the <c>GET /api/admin/ai-gateway/versions</c> endpoint.
    /// </summary>
    IReadOnlyDictionary<string, ModelVersionEntry> GetAllVersions();

    /// <summary>
    /// Atomically swaps the active and previous model versions for
    /// <paramref name="providerName"/>. Returns a <see cref="RollbackResponse"/>
    /// describing the outcome. Returns a failure response (not an exception) when
    /// no previous version is available or the provider is unknown.
    /// </summary>
    RollbackResponse ExecuteRollback(string providerName, string adminUserId, string reason);

    /// <summary>
    /// Synchronizes the registry with a new model version that arrived via
    /// an <c>IOptionsMonitor&lt;T&gt;.OnChange</c> callback (config file hot-patch).
    /// The current active version is preserved as the previous version for potential rollback.
    /// </summary>
    void SyncFromConfiguration(string providerName, string newModelId);
}
