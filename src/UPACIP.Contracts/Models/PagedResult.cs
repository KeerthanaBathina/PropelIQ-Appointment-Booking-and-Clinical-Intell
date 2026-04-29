namespace UPACIP.Contracts.Models;

/// <summary>
/// Generic paginated result container for all collection endpoints (AC-1, TR-009).
///
/// Service layer methods that return collections use <c>PagedResult&lt;T&gt;</c>
/// so that the Presentation layer never builds raw <c>IQueryable</c> or operates
/// on DataAccess types directly.
///
/// <para>
/// Computed properties (<see cref="TotalPages"/>, <see cref="HasNext"/>,
/// <see cref="HasPrevious"/>) are evaluated client-side from the primitive fields,
/// keeping the type serializable without custom converters.
/// </para>
/// </summary>
/// <typeparam name="T">The item type in the result set.</typeparam>
public sealed class PagedResult<T>
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
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalCount / (double)PageSize)
        : 0;

    /// <summary>True when a next page exists.</summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>True when a previous page exists.</summary>
    public bool HasPrevious => Page > 1;

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="PagedResult{T}"/> from a pre-fetched item list.</summary>
    public static PagedResult<T> Create(IEnumerable<T> items, int page, int pageSize, int totalCount) =>
        new() { Items = [..items], Page = page, PageSize = pageSize, TotalCount = totalCount };

    /// <summary>Creates an empty paged result for the given parameters.</summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalCount = 0 };
}
