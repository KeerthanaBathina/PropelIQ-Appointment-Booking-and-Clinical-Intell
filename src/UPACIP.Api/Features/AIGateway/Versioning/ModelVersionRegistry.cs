using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Resilience;

namespace UPACIP.Api.Features.AIGateway.Versioning;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IModelVersionRegistry"/>.
///
/// <para>Uses a <see cref="ReaderWriterLockSlim"/> for all state mutations so many concurrent
/// request threads can read the active version without blocking each other, while an admin
/// rollback briefly acquires an exclusive write lock (AIR-O05).</para>
///
/// <para><strong>Circuit breaker coordination (US_069 TASK_002):</strong>
/// When rolling back a provider whose circuit is <see cref="ProviderState.Unavailable"/>,
/// this registry transitions it to <see cref="ProviderState.Degraded"/> (half-open probe)
/// so the circuit breaker immediately tests the rolled-back version rather than waiting for
/// the full <c>BreakDuration</c> to elapse.</para>
/// </summary>
public sealed class ModelVersionRegistry : IModelVersionRegistry, IDisposable
{
    private readonly Dictionary<string, ModelVersionEntry> _versions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ReaderWriterLockSlim         _lock         = new();
    private readonly ProviderStateManager         _stateManager;
    private readonly ILogger<ModelVersionRegistry> _logger;

    // ── Supported model identifiers — validated before applying a config change ──

    private static readonly IReadOnlySet<string> KnownOpenAiModels = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "gpt-4o-mini",
        "gpt-4o",
        "gpt-4-turbo",
        "gpt-4",
        "gpt-3.5-turbo",
    };

    private static readonly IReadOnlySet<string> KnownClaudeModels = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "claude-3-5-sonnet-20241022",
        "claude-3-5-haiku-20241022",
        "claude-3-opus-20240229",
        "claude-3-sonnet-20240229",
        "claude-3-haiku-20240307",
    };

    // ── Constructor — seed from IOptionsMonitor; subscribe to OnChange ──────────

    public ModelVersionRegistry(
        IOptionsMonitor<OpenAIProviderOptions>  openAiMonitor,
        IOptionsMonitor<ClaudeProviderOptions>  claudeMonitor,
        ProviderStateManager                    stateManager,
        ILogger<ModelVersionRegistry>           logger)
    {
        _stateManager = stateManager;
        _logger        = logger;

        var now = DateTimeOffset.UtcNow;

        _versions["openai"] = new ModelVersionEntry(
            ProviderName:    "openai",
            ActiveModelId:   openAiMonitor.CurrentValue.Model,
            PreviousModelId: null,
            ActivatedAt:     now,
            ActivatedBy:     "system");

        _versions["anthropic"] = new ModelVersionEntry(
            ProviderName:    "anthropic",
            ActiveModelId:   claudeMonitor.CurrentValue.Model,
            PreviousModelId: null,
            ActivatedAt:     now,
            ActivatedBy:     "system");

        // Keep registry in sync when config files are hot-patched on disk.
        openAiMonitor.OnChange(opts => SyncFromConfiguration("openai", opts.Model));
        claudeMonitor.OnChange(opts => SyncFromConfiguration("anthropic", opts.Model));

        _logger.LogInformation(
            "ModelVersionRegistry initialized. " +
            "OpenAI={OpenAiModel} Claude={ClaudeModel}",
            openAiMonitor.CurrentValue.Model,
            claudeMonitor.CurrentValue.Model);
    }

    // ── IModelVersionRegistry ────────────────────────────────────────────────

    /// <inheritdoc/>
    public string GetActiveVersion(string providerName)
    {
        _lock.EnterReadLock();
        try
        {
            return _versions.TryGetValue(providerName, out var entry)
                ? entry.ActiveModelId
                : string.Empty;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc/>
    public string? GetPreviousVersion(string providerName)
    {
        _lock.EnterReadLock();
        try
        {
            return _versions.TryGetValue(providerName, out var entry)
                ? entry.PreviousModelId
                : null;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, ModelVersionEntry> GetAllVersions()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<string, ModelVersionEntry>(
                _versions, StringComparer.OrdinalIgnoreCase);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc/>
    public RollbackResponse ExecuteRollback(
        string providerName,
        string adminUserId,
        string reason)
    {
        var now = DateTimeOffset.UtcNow;

        _lock.EnterWriteLock();
        try
        {
            if (!_versions.TryGetValue(providerName, out var entry))
            {
                return new RollbackResponse(
                    Success:             false,
                    Provider:            providerName,
                    PreviousVersion:     string.Empty,
                    RolledBackToVersion: string.Empty,
                    Timestamp:           now,
                    ErrorMessage:        $"Provider '{providerName}' is not registered in the version registry.");
            }

            if (entry.PreviousModelId is null or { Length: 0 })
            {
                return new RollbackResponse(
                    Success:             false,
                    Provider:            providerName,
                    PreviousVersion:     entry.ActiveModelId,
                    RolledBackToVersion: string.Empty,
                    Timestamp:           now,
                    ErrorMessage:        $"No previous version available for provider '{providerName}'. " +
                                         "At least one prior activation is required to roll back.");
            }

            var fromVersion = entry.ActiveModelId;
            var toVersion   = entry.PreviousModelId;

            // Swap active ↔ previous.
            _versions[providerName] = new ModelVersionEntry(
                ProviderName:    providerName,
                ActiveModelId:   toVersion,
                PreviousModelId: fromVersion,
                ActivatedAt:     now,
                ActivatedBy:     adminUserId);

            // Log structured rollback audit event (no PII — model IDs are not patient data).
            _logger.LogWarning(
                "AI Gateway: model version rollback applied. " +
                "Provider={Provider} FromVersion={FromVersion} ToVersion={ToVersion} " +
                "AdminUserId={AdminUserId} Reason={Reason} Timestamp={Timestamp}",
                providerName, fromVersion, toVersion, adminUserId, reason, now);

            // ── Circuit breaker coordination ──────────────────────────────────
            // If the provider is Unavailable (circuit open), reset to Degraded (half-open)
            // so the rolled-back version is probed immediately without waiting for BreakDuration.
            var currentState = _stateManager.GetProviderState(providerName);
            if (currentState == ProviderState.Unavailable)
            {
                _stateManager.TransitionTo(providerName, ProviderState.Degraded);

                _logger.LogInformation(
                    "AI Gateway: circuit breaker reset to HalfOpen after rollback. " +
                    "Provider={Provider} CircuitStateBefore={Before} CircuitStateAfter={After}",
                    providerName, currentState, ProviderState.Degraded);
            }

            return new RollbackResponse(
                Success:             true,
                Provider:            providerName,
                PreviousVersion:     fromVersion,
                RolledBackToVersion: toVersion,
                Timestamp:           now);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc/>
    public void SyncFromConfiguration(string providerName, string newModelId)
    {
        if (!IsKnownModel(providerName, newModelId))
        {
            _logger.LogWarning(
                "ModelVersionRegistry: ignoring config change for provider '{Provider}' " +
                "— unknown model '{ModelId}'. Supported models were not updated.",
                providerName, newModelId);
            return;
        }

        _lock.EnterWriteLock();
        try
        {
            if (!_versions.TryGetValue(providerName, out var existing))
                return;

            if (string.Equals(existing.ActiveModelId, newModelId, StringComparison.OrdinalIgnoreCase))
                return; // No-op: config reloaded but model hasn't changed.

            _versions[providerName] = new ModelVersionEntry(
                ProviderName:    providerName,
                ActiveModelId:   newModelId,
                PreviousModelId: existing.ActiveModelId,
                ActivatedAt:     DateTimeOffset.UtcNow,
                ActivatedBy:     "system:config-reload");

            _logger.LogInformation(
                "ModelVersionRegistry: config-based model version update applied. " +
                "Provider={Provider} PreviousModel={Previous} NewModel={New}",
                providerName, existing.ActiveModelId, newModelId);
        }
        finally { _lock.ExitWriteLock(); }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => _lock.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsKnownModel(string providerName, string modelId) =>
        providerName.Equals("openai",    StringComparison.OrdinalIgnoreCase)
            ? KnownOpenAiModels.Contains(modelId)
            : providerName.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
                ? KnownClaudeModels.Contains(modelId)
                : true; // Unknown provider — allow the value (future-proofing).
}
