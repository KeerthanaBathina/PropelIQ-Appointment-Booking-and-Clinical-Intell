namespace UPACIP.Service.Appointments;

/// <summary>
/// Paginated response envelope for the patient appointment history endpoint (US_024, FR-024).
///
/// <para>
/// <strong>Empty-state behaviour (EC-1):</strong>
/// When the patient has no appointments, or when the requested page exceeds the total page
/// count, <see cref="Items"/> is an empty list and <see cref="TotalCount"/> is 0.
/// The endpoint always returns 200 OK — never 404 — for empty histories.
/// </para>
///
/// <para>
/// <strong>Pagination metadata:</strong>
/// <see cref="TotalCount"/> and <see cref="TotalPages"/> are computed in the service
/// and must be treated as authoritative by the frontend.
/// </para>
/// </summary>
/// <param name="Items">The appointment rows for the requested page. Empty list when no results.</param>
/// <param name="TotalCount">Total appointments matching the patient's history query (before pagination).</param>
/// <param name="TotalPages">Total pages at the fixed page size of <see cref="AppointmentHistoryQuery.PageSize"/>.</param>
/// <param name="Page">The page number returned (1-based).</param>
/// <param name="PageSize">The number of items per page (always <see cref="AppointmentHistoryQuery.PageSize"/>).</param>
public sealed record AppointmentHistoryResponse(
    IReadOnlyList<AppointmentHistoryItemDto> Items,
    int                                      TotalCount,
    int                                      TotalPages,
    int                                      Page,
    int                                      PageSize);
