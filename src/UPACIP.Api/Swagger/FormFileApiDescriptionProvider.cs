using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace UPACIP.Api.Swagger;

/// <summary>
/// Ensures that any ApiDescription with [FromForm] IFormFile parameters has
/// "multipart/form-data" in SupportedRequestFormats. This is required because
/// Asp.Versioning.Mvc.ApiExplorer does not always propagate the [Consumes] attribute
/// into SupportedRequestFormats, causing Swashbuckle 6.x to throw a
/// SwaggerGeneratorException for file-upload endpoints.
/// </summary>
internal sealed class FormFileApiDescriptionProvider : IApiDescriptionProvider
{
    // Run LAST in OnProvidersExecuted (lowest Order = last in descending execution).
    // DefaultApiDescriptionProvider=0, Asp.Versioning≈100; we use a very low value
    // so our OnProvidersExecuted runs after all others, making the additions stick.
    public int Order => int.MinValue + 1;

    public void OnProvidersExecuted(ApiDescriptionProviderContext context)
    {
        foreach (var description in context.Results)
        {
            // IFormFile parameters may have BindingSource.FormFile (not BindingSource.Form),
            // so check both sources. Also fall back to ParameterDescriptor.ParameterType
            // when ModelMetadata is not populated (e.g., Asp.Versioning-cloned descriptions).
            bool hasFormFileParam = description.ParameterDescriptions.Any(p =>
                (p.Source == BindingSource.Form || p.Source == BindingSource.FormFile) &&
                (
                    (p.ModelMetadata?.ModelType is { } mt && typeof(IFormFile).IsAssignableFrom(mt)) ||
                    (p.ParameterDescriptor?.ParameterType is { } pt && typeof(IFormFile).IsAssignableFrom(pt))
                ));

            if (!hasFormFileParam) continue;

            bool alreadyHasMultipart = description.SupportedRequestFormats.Any(f =>
                f.MediaType.Contains("multipart", StringComparison.OrdinalIgnoreCase));

            if (!alreadyHasMultipart)
            {
                description.SupportedRequestFormats.Add(new ApiRequestFormat
                {
                    MediaType = "multipart/form-data"
                });
            }
        }
    }

    public void OnProvidersExecuting(ApiDescriptionProviderContext context) { }
}
