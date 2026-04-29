using System.Reflection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using UPACIP.Api.Attributes;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Swashbuckle <see cref="IOperationFilter"/> that documents the <c>Idempotency-Key</c> header
/// and the associated 409/422 error responses on all operations whose action method (or declaring
/// controller type) is annotated with <see cref="IdempotentEndpointAttribute"/> (US_102, AC-3).
///
/// This filter is registered in <c>SwaggerConfiguration.AddSwaggerDocumentation</c> so it
/// applies across all API versions automatically.
/// </summary>
public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check the action method first, then fall back to the declaring type (class-level attribute).
        var attribute = context.MethodInfo.GetCustomAttribute<IdempotentEndpointAttribute>()
            ?? context.MethodInfo.DeclaringType?.GetCustomAttribute<IdempotentEndpointAttribute>();

        if (attribute is null)
            return;

        operation.Parameters ??= new List<OpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = attribute.Required,
            Description =
                "UUID v4 idempotency key (IETF draft). " +
                "Sending the same key with the same request body replays the cached response. " +
                "Sending the same key with a *different* body returns HTTP 422.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "uuid",
                Example = new OpenApiString("550e8400-e29b-41d4-a716-446655440000"),
            },
        });

        // Document the 409 Conflict response (concurrent duplicate / in-flight)
        operation.Responses.TryAdd("409", new OpenApiResponse
        {
            Description = "A request with this idempotency key is currently being processed. Retry after a short delay.",
        });

        // Document the 422 Unprocessable Entity response (key reused with different body)
        operation.Responses.TryAdd("422", new OpenApiResponse
        {
            Description = "Idempotency key was previously used with a different request body.",
        });
    }
}
