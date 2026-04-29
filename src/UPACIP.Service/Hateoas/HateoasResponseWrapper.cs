using UPACIP.Contracts.Models;

namespace UPACIP.Service.Hateoas;

/// <summary>
/// Wraps service results in HATEOAS-enriched response envelopes (US_096, AC-2, TR-011).
///
/// Controllers inject this class and call <see cref="WrapResource{T}"/> for single-resource
/// responses or <see cref="WrapCollection{T}"/> for paginated collection responses.
///
/// Scoped lifetime — delegates to <see cref="IHateoasLinkGenerator"/> which is also Scoped.
/// </summary>
public sealed class HateoasResponseWrapper
{
    private readonly IHateoasLinkGenerator _linkGenerator;

    public HateoasResponseWrapper(IHateoasLinkGenerator linkGenerator)
    {
        _linkGenerator = linkGenerator;
    }

    /// <summary>
    /// Wraps a single resource in a <see cref="HateoasResponse{T}"/> with <c>self</c>
    /// and optional related/action links.
    /// </summary>
    /// <typeparam name="T">Resource payload type.</typeparam>
    /// <param name="data">The resource payload.</param>
    /// <param name="routeName">Named route for the canonical self URL (e.g., <c>"GetPatientById"</c>).</param>
    /// <param name="routeValues">Route values for the self link (e.g., <c>new { id = patient.Id }</c>).</param>
    /// <param name="relatedLinks">
    /// Optional list of related-resource and action links. Each tuple:
    /// <c>(routeName, routeValues, rel, method)</c>.
    /// Action links (PUT/DELETE) should only be passed when the caller's role permits the action.
    /// </param>
    /// <param name="message">Optional human-readable message to surface in the response.</param>
    /// <returns><see cref="HateoasResponse{T}"/> with <see cref="HateoasResponse{T}.Success"/> = true.</returns>
    public HateoasResponse<T> WrapResource<T>(
        T data,
        string routeName,
        object? routeValues,
        List<(string RouteName, object? RouteValues, string Rel, string Method)>? relatedLinks = null,
        string? message = null)
    {
        var links = _linkGenerator.GenerateResourceLinks(
            routeName,
            routeValues,
            relatedLinks ?? []);

        return new HateoasResponse<T>
        {
            Data = data,
            Success = true,
            Message = message,
            Links = links
        };
    }

    /// <summary>
    /// Wraps a <see cref="PagedResult{T}"/> in a <see cref="PagedHateoasResponse{T}"/>
    /// with full pagination links (self, first, last, next, previous) (edge case 2).
    /// </summary>
    /// <typeparam name="T">Item type in the collection.</typeparam>
    /// <param name="pagedResult">Paginated result from the service layer.</param>
    /// <param name="routeName">Named route for the collection endpoint (e.g., <c>"GetPatients"</c>).</param>
    /// <returns>
    /// <see cref="PagedHateoasResponse{T}"/> with pagination metadata and RFC 5988 links.
    /// </returns>
    public PagedHateoasResponse<T> WrapCollection<T>(
        PagedResult<T> pagedResult,
        string routeName)
    {
        var links = _linkGenerator.GeneratePaginationLinks(
            routeName,
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalPages);

        return new PagedHateoasResponse<T>
        {
            Items = pagedResult.Items,
            Page = pagedResult.Page,
            PageSize = pagedResult.PageSize,
            TotalCount = pagedResult.TotalCount,
            TotalPages = pagedResult.TotalPages,
            Links = links
        };
    }
}
