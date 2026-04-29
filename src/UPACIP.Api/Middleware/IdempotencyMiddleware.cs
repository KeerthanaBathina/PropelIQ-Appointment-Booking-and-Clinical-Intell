using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Attributes;
using UPACIP.Service.Idempotency;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core convention-based middleware that provides idempotency guarantees for
/// state-changing endpoints marked with <see cref="IdempotentEndpointAttribute"/>
/// (US_102, AC-1, edge case 1).
///
/// Flow:
/// <list type="number">
///   <item>Skip non-mutating methods (GET, HEAD, OPTIONS) and endpoints without the attribute.</item>
///   <item>Validate the <c>Idempotency-Key</c> header format (UUID).</item>
///   <item>Look up the key in Redis (<see cref="IIdempotencyStore"/>).</item>
///   <item>
///     If found and completed → replay cached response with <c>Idempotency-Replayed: true</c> header.
///   </item>
///   <item>
///     If found and in-flight → return 409 Conflict (concurrent duplicate).
///   </item>
///   <item>
///     If found with a different body hash → return 422 (edge case 1: key reuse with different payload).
///   </item>
///   <item>
///     If not found → create in-flight record (atomic SET NX), capture response, update to completed.
///   </item>
///   <item>On exception → remove the in-flight record so the client may retry with the same key.</item>
/// </list>
///
/// Thread safety:
///   Concurrent duplicate requests are handled by the atomic <c>SET NX</c> in
///   <see cref="IIdempotencyStore.TryCreateAsync"/>. The loser of the race receives 409 Conflict.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IIdempotencyStore store,
        IOptions<IdempotencyOptions> options)
    {
        // Skip non-mutating HTTP methods
        if (!MutatingMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Skip endpoints not marked with [IdempotentEndpoint]
        var endpoint = context.GetEndpoint();
        var attribute = endpoint?.Metadata.GetMetadata<IdempotentEndpointAttribute>();
        if (attribute is null)
        {
            await _next(context);
            return;
        }

        var opts = options.Value;
        var idempotencyKey = context.Request.Headers[opts.HeaderName].FirstOrDefault();

        // Missing header
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (attribute.Required)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Missing required Idempotency-Key header",
                    detail = $"Include a UUID v4 value in the '{opts.HeaderName}' header."
                });
                return;
            }

            await _next(context);
            return;
        }

        // Validate UUID format (must parse as a valid Guid)
        if (!Guid.TryParse(idempotencyKey, out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Invalid Idempotency-Key format",
                detail = "Idempotency-Key must be a valid UUID v4 (e.g. 550e8400-e29b-41d4-a716-446655440000)."
            });
            return;
        }

        // Compute SHA-256 hash of the raw request body for mismatch detection (edge case 1).
        // EnableBuffering() allows the body stream to be read more than once.
        context.Request.EnableBuffering();
        var bodyBytes = await ReadRequestBodyAsync(context.Request);
        var bodyHash = ComputeSha256Hex(bodyBytes);
        context.Request.Body.Position = 0;

        // Look up existing record
        var existing = await store.GetAsync(idempotencyKey, context.RequestAborted);

        if (existing is not null)
        {
            // Edge case 1: same key, different body → 422
            if (existing.RequestBodyHash != bodyHash)
            {
                _logger.LogWarning(
                    "IDEMPOTENCY_KEY_MISMATCH: Key={Key}, StoredHash={StoredHash}, IncomingHash={IncomingHash}",
                    idempotencyKey, existing.RequestBodyHash, bodyHash);

                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Idempotency key mismatch",
                    detail = "This idempotency key was previously used with a different request body. " +
                             "Generate a new key for a different request."
                });
                return;
            }

            // Completed: replay cached response
            if (existing.IsCompleted)
            {
                _logger.LogInformation(
                    "IDEMPOTENCY_REPLAY: Key={Key}, CachedStatusCode={StatusCode}",
                    idempotencyKey, existing.StatusCode);

                context.Response.StatusCode = existing.StatusCode;
                context.Response.Headers.Append("Idempotency-Replayed", "true");

                foreach (var (headerName, headerValue) in existing.ResponseHeaders)
                {
                    context.Response.Headers[headerName] = headerValue;
                }

                if (existing.ResponseBody is not null)
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(existing.ResponseBody);
                }

                return;
            }

            // In-flight: another request with this key is currently processing
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Request in progress",
                detail = "A request with this idempotency key is currently being processed. " +
                         "Retry after a short delay."
            });
            return;
        }

        // No existing record — attempt atomic creation (SET NX)
        var record = new IdempotencyRecord
        {
            Key = idempotencyKey,
            RequestBodyHash = bodyHash,
            IsCompleted = false,
        };

        var created = await store.TryCreateAsync(record, context.RequestAborted);
        if (!created)
        {
            // Lost race to another concurrent duplicate
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Request in progress",
                detail = "A request with this idempotency key is currently being processed. " +
                         "Retry after a short delay."
            });
            return;
        }

        // Swap the response body stream so we can capture the output
        var originalBody = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await _next(context);

            // Capture and cache the completed response
            memoryStream.Position = 0;
            var responseBodyText = await new StreamReader(memoryStream).ReadToEndAsync();

            record.StatusCode = context.Response.StatusCode;
            record.IsCompleted = true;
            record.ResponseHeaders["Content-Type"] =
                context.Response.ContentType ?? "application/json";

            // Only cache if within the size limit
            if (responseBodyText.Length <= opts.MaxBodySizeBytes)
            {
                record.ResponseBody = responseBodyText;
            }

            await store.UpdateAsync(record, context.RequestAborted);

            // Write the captured bytes back to the real response stream
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBody);
        }
        catch
        {
            // Remove the in-flight record so the client can retry with the same key
            await store.RemoveAsync(idempotencyKey);
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request)
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes);
    }
}
