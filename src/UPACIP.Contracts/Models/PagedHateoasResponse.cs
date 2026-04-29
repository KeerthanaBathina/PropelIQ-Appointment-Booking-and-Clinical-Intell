namespace UPACIP.Contracts.Models;

/// <summary>
/// HATEOAS-enriched paginated collection response (US_096, AC-2, TR-011, edge case 2).
///
/// Extends <see cref="PagedResult{T}"/> with a full set of RFC 5988 pagination links:
/// <c>self</c>, <c>first</c>, <c>last</c>, conditional <c>next</c>, and conditional
/// <c>previous</c>. Also surfaces page count metadata so clients can render paginators
/// without additional requests.
///
/// Usage in controllers:
/// <code>
/// var paged = await _service.GetAppointmentsAsync(page, pageSize);
/// var hateoas = _wrapper.WrapCollection(paged, "GetAppointments");
/// return Ok(hateoas);
/// </code>
/// </summary>
/// <typeparam name="T">The item type in the result set.</typeparam>
public sealed class PagedHateoasResponse<T>
{
    /// <summary>Items in the current page.</summary>
    public List<T> Items { get; init; } = [];

    /// <summary>1-based current page number.</summary>
    public int Page { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of items across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages calculated from <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Pagination and navigation links.
    /// Always contains <c>self</c>, <c>first</c>, and <c>last</c>.
    /// Contains <c>next</c> when not on the last page.
    /// Contains <c>previous</c> when not on the first page.
    /// </summary>
    public List<HateoasLink> Links { get; init; } = [];
}
