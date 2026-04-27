namespace UPACIP.Service.Profile;

/// <summary>
/// Staff-facing patient search service (US_062 AC-1).
/// Provides fuzzy patient lookup with provider/status filters and a provider dropdown list.
/// </summary>
public interface IPatientSearchService
{
    /// <summary>
    /// Executes a paginated patient search with optional provider and status filters.
    /// Results are cached per unique query; an audit log entry is written per invocation.
    /// </summary>
    Task<PatientSearchResponseDto> SearchPatientsAsync(
        PatientSearchQuery query,
        Guid actingUserId,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the list of distinct provider names available as search filter options.
    /// Results are cached for 5 minutes (NFR-030).
    /// </summary>
    Task<IReadOnlyList<ProviderOptionDto>> GetProviderListAsync(CancellationToken ct = default);
}
