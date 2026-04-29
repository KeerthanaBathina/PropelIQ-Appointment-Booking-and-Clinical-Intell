using Polly;

namespace UPACIP.Api.Http;

/// <summary>
/// <see cref="DelegatingHandler"/> that wraps all outbound HTTP requests in the
/// <c>http-retry</c> Polly V8 resilience pipeline (US_095, AC-2, NFR-023).
///
/// Attach to named <see cref="System.Net.Http.HttpClient"/> registrations via:
/// <code>
/// builder.Services.AddHttpClient("name", ...).AddHttpMessageHandler&lt;TransientResilienceHttpHandler&gt;();
/// </code>
///
/// The handler is registered as <b>Transient</b> (required by <see cref="IHttpMessageHandlerFactory"/>
/// lifetime rules — each named HttpClient instance gets its own handler chain).
///
/// <para>
/// The pipeline is resolved lazily (first request) via <see cref="IServiceProvider"/> to
/// avoid a circular dependency between the handler factory and the Singleton registry.
/// This pattern is identical to how <c>IHttpClientFactory</c> injects scoped services.
/// </para>
/// </summary>
public sealed class TransientResilienceHttpHandler : DelegatingHandler
{
    private readonly UPACIP.Service.Resilience.TransientResiliencePipelineRegistry _registry;

    public TransientResilienceHttpHandler(
        UPACIP.Service.Resilience.TransientResiliencePipelineRegistry registry)
    {
        _registry = registry;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage  request,
        CancellationToken   cancellationToken)
    {
        HttpResponseMessage? response = null;
        await _registry.Http.ExecuteAsync(
            async ct => { response = await base.SendAsync(request, ct); },
            cancellationToken);
        return response!;
    }
}
