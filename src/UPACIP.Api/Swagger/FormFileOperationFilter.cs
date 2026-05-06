using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Moves <see cref="IFormFile"/> parameters from the generated query-parameter list into
/// the <c>multipart/form-data</c> request body schema.
///
/// Swashbuckle 6.x excludes <c>IFormFile</c> from its "form parameters" collection
/// (<c>IsFromForm()</c> returns <c>false</c> when <c>ModelType == typeof(IFormFile)</c>),
/// so without this filter the file field would end up as a query parameter.
/// </summary>
internal sealed class FormFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var fileParams = context.ApiDescription.ParameterDescriptions
            .Where(p =>
                (p.Source == BindingSource.Form || p.Source == BindingSource.FormFile) &&
                (
                    (p.ModelMetadata?.ModelType is { } mt && typeof(IFormFile).IsAssignableFrom(mt)) ||
                    (p.ParameterDescriptor?.ParameterType is { } pt && typeof(IFormFile).IsAssignableFrom(pt))
                ))
            .ToList();

        if (fileParams.Count == 0) return;

        // Remove file params that Swashbuckle incorrectly placed as query parameters.
        if (operation.Parameters != null)
        {
            foreach (var fileParam in fileParams)
            {
                var wrongParam = operation.Parameters
                    .FirstOrDefault(p => p.Name == fileParam.Name);
                if (wrongParam != null)
                    operation.Parameters.Remove(wrongParam);
            }
        }

        // Ensure the request body exists with multipart/form-data content.
        operation.RequestBody ??= new OpenApiRequestBody { Required = true };

        if (!operation.RequestBody.Content.TryGetValue("multipart/form-data", out var mediaType))
        {
            mediaType = new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Type       = "object",
                    Properties = new Dictionary<string, OpenApiSchema>()
                }
            };
            operation.RequestBody.Content["multipart/form-data"] = mediaType;
        }

        mediaType.Schema ??= new OpenApiSchema
        {
            Type       = "object",
            Properties = new Dictionary<string, OpenApiSchema>()
        };

        // Add each IFormFile as a binary property in the multipart body schema.
        foreach (var fileParam in fileParams)
        {
            if (!mediaType.Schema.Properties.ContainsKey(fileParam.Name))
            {
                mediaType.Schema.Properties[fileParam.Name] = new OpenApiSchema
                {
                    Type   = "string",
                    Format = "binary"
                };
            }
        }
    }
}
