namespace UPACIP.Service.Profile;

/// <summary>
/// Inbound query parameters for the staff patient search endpoint (US_062 AC-1).
/// </summary>
public sealed record PatientSearchQuery(
    string Term,
    string Provider,
    string Status,
    int Page,
    int PageSize);

/// <summary>
/// Single patient row returned by the search results page (SCR-016).
/// </summary>
public sealed record PatientSummaryDto(
    string PatientId,
    string FullName,
    string Mrn,
    string DateOfBirth,
    string Phone,
    string Provider,
    string? LastVisitAt,
    string Status);

/// <summary>
/// Paginated patient search response envelope (US_062 AC-1).
/// </summary>
public sealed record PatientSearchResponseDto(
    IReadOnlyList<PatientSummaryDto> Patients,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Provider option for the search filter dropdown (US_062 AC-1).
/// </summary>
public sealed record ProviderOptionDto(string Id, string DisplayName);
