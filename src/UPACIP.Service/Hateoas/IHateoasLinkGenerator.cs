using UPACIP.Contracts.Models;

namespace UPACIP.Service.Hateoas;

/// <summary>
/// Generates RFC 5988-compliant hypermedia links from ASP.NET Core named routes
/// (US_096, AC-2, TR-011).
///
/// Implementations use <c>LinkGenerator</c> to produce absolute URLs, ensuring
/// correct behaviour behind reverse proxies when <c>ForwardedHeadersOptions</c> is
/// configured.
/// </summary>
public interface IHateoasLinkGenerator
{
    /// <summary>
    /// Creates a single <see cref="HateoasLink"/> from a named route.
    /// </summary>
    /// <param name="routeName">The route name registered on the controller action (e.g., <c>"GetPatientById"</c>).</param>
    /// <param name="routeValues">Route and query-string values used to build the URL.</param>
    /// <param name="rel">Link relation type (e.g., <c>"self"</c>, <c>"next"</c>, <c>"appointments"</c>).</param>
    /// <param name="method">HTTP method to use when following the link.</param>
    /// <returns>A <see cref="HateoasLink"/> with an absolute <c>Href</c>.</returns>
    HateoasLink GenerateLink(string routeName, object? routeValues, string rel, string method);

    /// <summary>
    /// Creates a <c>self</c> link plus optional related-resource links for a single resource.
    /// </summary>
    /// <param name="resourceRouteName">Named route that identifies the canonical URL for the resource.</param>
    /// <param name="routeValues">Route values for the resource (e.g., <c>new { id = guid }</c>).</param>
    /// <param name="relatedLinks">
    /// Additional links to append. Each tuple contains
    /// <c>(routeName, routeValues, rel, method)</c>.
    /// </param>
    /// <returns>A list starting with the <c>self</c> link followed by the related links.</returns>
    List<HateoasLink> GenerateResourceLinks(
        string resourceRouteName,
        object? routeValues,
        List<(string RouteName, object? RouteValues, string Rel, string Method)> relatedLinks);

    /// <summary>
    /// Creates the full set of pagination links for a collection response (edge case 2).
    ///
    /// Always includes <c>self</c>, <c>first</c>, and <c>last</c>.
    /// Includes <c>next</c> when <paramref name="page"/> &lt; <paramref name="totalPages"/>.
    /// Includes <c>previous</c> when <paramref name="page"/> &gt; 1.
    /// </summary>
    /// <param name="routeName">Named route for the collection endpoint.</param>
    /// <param name="page">Current 1-based page number.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="totalPages">Total page count.</param>
    /// <returns>Pagination links with absolute URLs.</returns>
    List<HateoasLink> GeneratePaginationLinks(string routeName, int page, int pageSize, int totalPages);
}
