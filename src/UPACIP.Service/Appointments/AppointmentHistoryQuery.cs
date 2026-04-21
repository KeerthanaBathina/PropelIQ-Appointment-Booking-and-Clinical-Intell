using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Query parameters for the paginated patient appointment history endpoint (US_024, FR-024).
///
/// <para>
/// PatientId is NOT accepted from the client — it is resolved server-side from the
/// authenticated user's JWT email claim to prevent IDOR attacks (OWASP A01).
/// </para>
///
/// <para>
/// <strong>Page size:</strong> Fixed at <see cref="PageSize"/> (10) per AC-3.
/// The client controls which page to retrieve via <see cref="Page"/>.
/// </para>
///
/// <para>
/// <strong>Sort:</strong> Default is <c>desc</c> (newest-first, AC-1).
/// The client may request <c>asc</c> (oldest-first) via <see cref="SortDirection"/>.
/// Only the date column is sortable (AC-2).
/// </para>
/// </summary>
public sealed record AppointmentHistoryQuery
{
    /// <summary>Fixed page size per AC-3. Not configurable by the client.</summary>
    public const int PageSize = 10;

    /// <summary>
    /// One-based page number. Defaults to 1.
    /// Pages beyond the total page count return an empty items list with accurate metadata.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Sort direction applied to <c>AppointmentTime</c>.
    /// Accepts <c>asc</c> or <c>desc</c> (case-insensitive).
    /// Defaults to <c>desc</c> (newest-first) per AC-1.
    /// </summary>
    public string SortDirection { get; init; } = "desc";
}
