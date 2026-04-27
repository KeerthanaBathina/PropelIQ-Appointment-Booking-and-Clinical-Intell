namespace UPACIP.Api.Features.AIGateway.Versioning;

/// <summary>
/// Response envelope returned by the rollback endpoint and by
/// <see cref="IModelVersionRegistry.ExecuteRollback"/> (US_069 TASK_003, AIR-O05).
/// </summary>
/// <param name="Success">
/// <see langword="true"/> when the rollback completed; <see langword="false"/> otherwise
/// (e.g., no previous version available, invalid provider).
/// </param>
/// <param name="Provider">
/// Normalized provider identifier (e.g., <c>"openai"</c>, <c>"anthropic"</c>).
/// </param>
/// <param name="PreviousVersion">
/// The model identifier that was active <em>before</em> this rollback (may be empty on failure).
/// </param>
/// <param name="RolledBackToVersion">
/// The model identifier now active after the rollback (may be empty on failure).
/// </param>
/// <param name="Timestamp">
/// UTC timestamp at which the rollback was applied.
/// </param>
/// <param name="ErrorMessage">
/// Human-readable error description when <see cref="Success"/> is <see langword="false"/>;
/// <see langword="null"/> on success.
/// </param>
public sealed record RollbackResponse(
    bool           Success,
    string         Provider,
    string         PreviousVersion,
    string         RolledBackToVersion,
    DateTimeOffset Timestamp,
    string?        ErrorMessage = null);
