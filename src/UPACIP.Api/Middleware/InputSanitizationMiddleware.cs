using System.Text;
using System.Text.Json;
using UPACIP.Api.Validation;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core middleware that sanitizes all incoming request inputs against XSS
/// and command injection before they reach business logic (US_066 AC-1, FR-095, NFR-018,
/// OWASP A03 — Injection).
///
/// Sanitization strategy:
///   • JSON bodies  — body is buffered (EnableBuffering), the JSON tree is walked
///                    recursively, and every string value is sanitized via
///                    <see cref="SanitizationExtensions.SanitizeForXss"/> and
///                    <see cref="SanitizationExtensions.SanitizeForCommandInjection"/>.
///                    The body stream is then replaced with the sanitized payload so that
///                    downstream model binding receives clean data.
///   • Query strings — each value is sanitized in-place before routing.
///
/// SQL injection protection is NOT performed here. EF Core parameterized queries are the
/// primary and correct layer for SQL injection prevention (EC-1: "O'Brien" is preserved).
///
/// Logging: a structured warning is emitted (without the original value) when any
/// sanitization modifies input. This supports security monitoring without exposing PII.
///
/// Placement in the pipeline: after authentication / session management, before routing
/// and authorization. Register via <see cref="UseInputSanitizationExtension.UseInputSanitization"/>.
/// </summary>
public sealed class InputSanitizationMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null, // preserve original casing from parsed JSON
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<InputSanitizationMiddleware> _logger;

    public InputSanitizationMiddleware(
        RequestDelegate next,
        ILogger<InputSanitizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        SanitizeQueryString(context);
        await SanitizeRequestBodyAsync(context);
        await _next(context);
    }

    // ── Query string sanitization ────────────────────────────────────────────

    private void SanitizeQueryString(HttpContext context)
    {
        var query = context.Request.Query;
        if (query.Count == 0) return;

        bool modified = false;
        var sanitized = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(
            query.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, values) in query)
        {
            var sanitizedValues = values.Select(v =>
            {
                if (v is null) return v;
                var result = v.SanitizeForXss().SanitizeForCommandInjection();
                if (result != v) modified = true;
                return result;
            }).ToArray();

            sanitized[key] = new Microsoft.Extensions.Primitives.StringValues(sanitizedValues);
        }

        if (!modified) return;

        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? string.Empty;
        _logger.LogWarning(
            "Input sanitization: query string parameter modified. CorrelationId={CorrelationId} Path={Path}",
            correlationId, context.Request.Path.Value);

        context.Request.QueryString = QueryString.Create(
            sanitized.Select(kvp => KeyValuePair.Create(kvp.Key, (string?)kvp.Value.ToString())));
    }

    // ── Request body sanitization ────────────────────────────────────────────

    private async Task SanitizeRequestBodyAsync(HttpContext context)
    {
        var request = context.Request;

        if (!IsJsonContentType(request.ContentType)) return;
        if (request.ContentLength is 0) return;

        // EnableBuffering replaces the request body stream with a rewindable MemoryStream
        // (or FileBufferingReadStream for large bodies). This allows downstream model binding
        // to read the body after this middleware has consumed and replaced it.
        request.EnableBuffering();

        string originalBody;
        using (var reader = new StreamReader(
            request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true))
        {
            originalBody = await reader.ReadToEndAsync(context.RequestAborted);
        }

        // Reset so downstream model binding can read from the start
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(originalBody)) return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(originalBody);
        }
        catch (JsonException)
        {
            // Malformed JSON — pass through; model binding will return a standard 400.
            return;
        }

        using (doc)
        {
            bool modified = false;
            var sanitizedRoot = SanitizeJsonElement(doc.RootElement, ref modified);

            if (!modified) return;

            var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                                ?? string.Empty;
            _logger.LogWarning(
                "Input sanitization: request body string value modified. CorrelationId={CorrelationId} Path={Path}",
                correlationId, request.Path.Value);

            var sanitizedJson   = JsonSerializer.Serialize(sanitizedRoot, SerializerOptions);
            var sanitizedBytes  = Encoding.UTF8.GetBytes(sanitizedJson);

            // Replace body stream with sanitized content.
            // Set ContentLength so downstream middleware / model binding knows the new size.
            request.Body          = new MemoryStream(sanitizedBytes);
            request.ContentLength = sanitizedBytes.Length;
        }
    }

    // ── JSON tree walker ─────────────────────────────────────────────────────

    private static object? SanitizeJsonElement(JsonElement element, ref bool modified)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject())
                    obj[prop.Name] = SanitizeJsonElement(prop.Value, ref modified);
                return obj;

            case JsonValueKind.Array:
                var arr = new List<object?>(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                    arr.Add(SanitizeJsonElement(item, ref modified));
                return arr;

            case JsonValueKind.String:
                var raw    = element.GetString() ?? string.Empty;
                var result = raw.SanitizeForXss().SanitizeForCommandInjection();
                if (result != raw) modified = true;
                return result;

            case JsonValueKind.Number:
                // Preserve numeric precision: prefer long, fall back to double.
                return element.TryGetInt64(out var lng) ? (object)lng : element.GetDouble();

            case JsonValueKind.True:  return true;
            case JsonValueKind.False: return false;

            // JsonValueKind.Null and undefined:
            default: return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsJsonContentType(string? contentType) =>
        contentType is not null &&
        (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("text/json",        StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Extension method for clean pipeline registration:
/// <c>app.UseInputSanitization();</c>
/// </summary>
public static class UseInputSanitizationExtension
{
    public static IApplicationBuilder UseInputSanitization(this IApplicationBuilder app)
        => app.UseMiddleware<InputSanitizationMiddleware>();
}
