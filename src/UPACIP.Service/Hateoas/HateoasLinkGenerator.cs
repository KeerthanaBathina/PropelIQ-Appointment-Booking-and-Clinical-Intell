using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using UPACIP.Contracts.Models;

namespace UPACIP.Service.Hateoas;

/// <summary>
/// Generates absolute HATEOAS links using ASP.NET Core's built-in <see cref="LinkGenerator"/>
/// (US_096, AC-2, TR-011).
///
/// Uses <see cref="IHttpContextAccessor"/> to obtain the current request context so that
/// generated URLs are scoped to the correct scheme and host. When <c>ForwardedHeadersOptions</c>
/// is configured in the pipeline, <c>X-Forwarded-Host</c> / <c>X-Forwarded-Proto</c> values
/// are automatically reflected in generated URLs.
///
/// Scoped lifetime — one instance per HTTP request to align with <see cref="IHttpContextAccessor"/>.
/// </summary>
public sealed class HateoasLinkGenerator : IHateoasLinkGenerator
{
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HateoasLinkGenerator> _logger;

    public HateoasLinkGenerator(
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HateoasLinkGenerator> logger)
    {
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc/>
    public HateoasLink GenerateLink(string routeName, object? routeValues, string rel, string method)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        string? href = null;

        if (httpContext is not null)
        {
            href = _linkGenerator.GetUriByRouteValues(
                httpContext,
                routeName,
                routeValues);
        }

        if (href is null)
        {
            // Fallback: return an empty href rather than a null object — clients can detect
            // missing links via empty string rather than receiving null.
            _logger.LogWarning(
                "HATEOAS_LINK_FAILED Route={RouteName} Rel={Rel} — could not generate absolute URI. " +
                "Ensure the route is registered and IHttpContextAccessor is configured.",
                routeName, rel);

            href = string.Empty;
        }

        return new HateoasLink { Href = href, Rel = rel, Method = method.ToUpperInvariant() };
    }

    /// <inheritdoc/>
    public List<HateoasLink> GenerateResourceLinks(
        string resourceRouteName,
        object? routeValues,
        List<(string RouteName, object? RouteValues, string Rel, string Method)> relatedLinks)
    {
        var links = new List<HateoasLink>(relatedLinks.Count + 1)
        {
            GenerateLink(resourceRouteName, routeValues, "self", "GET")
        };

        foreach (var (routeName, relRouteValues, rel, method) in relatedLinks)
        {
            links.Add(GenerateLink(routeName, relRouteValues, rel, method));
        }

        return links;
    }

    /// <inheritdoc/>
    public List<HateoasLink> GeneratePaginationLinks(
        string routeName, int page, int pageSize, int totalPages)
    {
        var links = new List<HateoasLink>(5)
        {
            GenerateLink(routeName, new { page, pageSize }, "self", "GET"),
            GenerateLink(routeName, new { page = 1, pageSize }, "first", "GET"),
            GenerateLink(routeName, new { page = totalPages > 0 ? totalPages : 1, pageSize }, "last", "GET")
        };

        if (page < totalPages)
            links.Add(GenerateLink(routeName, new { page = page + 1, pageSize }, "next", "GET"));

        if (page > 1)
            links.Add(GenerateLink(routeName, new { page = page - 1, pageSize }, "previous", "GET"));

        return links;
    }
}
