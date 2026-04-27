namespace UPACIP.Api.Features.AIGateway.Versioning;

/// <summary>
/// Immutable snapshot of the model version state for a single AI provider at a point in time
/// (US_069 TASK_003, AIR-O05).
///
/// <para>
/// Stored in <see cref="ModelVersionRegistry"/>'s per-provider dictionary.
/// On each rollback the registry replaces this record so callers always observe a
/// consistent before/after pair.
/// </para>
/// </summary>
/// <param name="ProviderName">
/// Normalized provider identifier (e.g., <c>"openai"</c>, <c>"anthropic"</c>).
/// Matches <see cref="Contracts.IAIProviderAdapter.ProviderName"/>.
/// </param>
/// <param name="ActiveModelId">
/// The model identifier currently in use (e.g., <c>"gpt-4o-mini"</c>,
/// <c>"claude-3-5-sonnet-20241022"</c>).
/// </param>
/// <param name="PreviousModelId">
/// The model identifier that was in use before the most recent activation or rollback.
/// <see langword="null"/> when this is the initial version recorded at startup.
/// </param>
/// <param name="ActivatedAt">
/// UTC timestamp at which the active version became current.
/// </param>
/// <param name="ActivatedBy">
/// Admin user ID that triggered the version change, or <c>"system"</c> for the initial
/// population at application startup.
/// </param>
public sealed record ModelVersionEntry(
    string         ProviderName,
    string         ActiveModelId,
    string?        PreviousModelId,
    DateTimeOffset ActivatedAt,
    string         ActivatedBy);
