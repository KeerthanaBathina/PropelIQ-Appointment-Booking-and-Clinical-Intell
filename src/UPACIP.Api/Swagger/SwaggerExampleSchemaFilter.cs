using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using UPACIP.Contracts.Models;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Injects realistic example values into Swagger schema definitions so the Swagger UI
/// "Try it out" feature pre-populates request bodies with representative data (US_098, AC-2).
///
/// Additional DTO examples can be added by appending further <c>if</c> branches following
/// the same pattern.
/// </summary>
public sealed class SwaggerExampleSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(AuditLogEntry))
        {
            schema.Example = new OpenApiObject
            {
                ["userId"]        = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
                ["action"]        = new OpenApiString("DataModify"),
                ["entityType"]    = new OpenApiString("Patient"),
                ["entityId"]      = new OpenApiString("7c9e6679-7425-40de-944b-e07fc1f90ae7"),
                ["oldValues"]     = new OpenApiString("{\"status\":\"Active\"}"),
                ["newValues"]     = new OpenApiString("{\"status\":\"Inactive\"}"),
                ["ipAddress"]     = new OpenApiString("192.168.1.100"),
                ["correlationId"] = new OpenApiString("a1b2c3d4-0000-0000-0000-000000000001"),
            };
        }
        else if (context.Type == typeof(AuditLogQueryFilter))
        {
            schema.Example = new OpenApiObject
            {
                ["fromUtc"]    = new OpenApiString("2026-01-01T00:00:00Z"),
                ["toUtc"]      = new OpenApiString("2026-12-31T23:59:59Z"),
                ["entityType"] = new OpenApiString("Patient"),
                ["page"]       = new OpenApiInteger(1),
                ["pageSize"]   = new OpenApiInteger(50),
            };
        }
    }
}
