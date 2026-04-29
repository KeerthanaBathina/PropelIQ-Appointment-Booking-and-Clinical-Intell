namespace UPACIP.Contracts.Models;

/// <summary>
/// Represents a single hypermedia link returned in a HATEOAS-compliant API response
/// (US_096, AC-2, TR-011).
///
/// Links are embedded in every resource and collection response so that API clients can
/// navigate the application graph without hard-coding URLs (Richardson Maturity Level 3).
/// </summary>
public sealed class HateoasLink
{
    /// <summary>
    /// Absolute URL to the target resource (e.g., <c>https://host/api/patients/123</c>).
    /// Generated via ASP.NET Core <c>LinkGenerator</c> which respects
    /// <c>X-Forwarded-Host</c> / <c>X-Forwarded-Proto</c> reverse-proxy headers.
    /// </summary>
    public string Href { get; init; } = string.Empty;

    /// <summary>
    /// IANA link relation type:
    /// <list type="bullet">
    ///   <item><c>self</c> — canonical URL for the current resource.</item>
    ///   <item><c>next</c> — next page in a paginated collection.</item>
    ///   <item><c>previous</c> — previous page in a paginated collection.</item>
    ///   <item><c>first</c> — first page of a paginated collection.</item>
    ///   <item><c>last</c> — last page of a paginated collection.</item>
    ///   <item><c>collection</c> — parent collection of a single resource.</item>
    ///   <item>Custom relations such as <c>appointments</c>, <c>auditLogs</c>.</item>
    /// </list>
    /// </summary>
    public string Rel { get; init; } = string.Empty;

    /// <summary>HTTP method to use when following the link: GET, POST, PUT, DELETE.</summary>
    public string Method { get; init; } = "GET";
}
