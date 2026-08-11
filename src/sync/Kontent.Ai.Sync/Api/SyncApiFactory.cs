using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace Kontent.Ai.Sync.Api;

/// <summary>
/// Builds the configured <see cref="HttpClient"/> and Refit client for the Sync API, for clients created
/// outside a container. The handler chain lives here so the standalone path and the DI path assemble the
/// same pipeline in the same order rather than drifting apart.
/// </summary>
internal static class SyncApiFactory
{
    /// <summary>
    /// Builds an <see cref="HttpClient"/> with the handler chain, outermost to innermost,
    /// <c>[resilience] → tracking → auth → primary</c> - matching the DI path's ordering so each retry
    /// re-runs tracking and auth fresh. Resilience is included only when <paramref name="resiliencePipeline"/>
    /// is supplied; <paramref name="primaryHandler"/> overrides the default <see cref="HttpClientHandler"/>.
    /// <paramref name="pipelineBoundsAttempts"/> says whether that pipeline is the SDK's default one, which
    /// carries the per-attempt timeout - only that one earns the removal of <see cref="HttpClient"/>'s ceiling.
    /// </summary>
    public static HttpClient CreateHttpClient(
        SyncOptions options,
        ISyncOptionsAccessor optionsAccessor,
        ResiliencePipeline<HttpResponseMessage>? resiliencePipeline = null,
        bool pipelineBoundsAttempts = false,
        ILoggerFactory? loggerFactory = null,
        HttpMessageHandler? primaryHandler = null)
    {
        HttpMessageHandler primary = primaryHandler ?? new HttpClientHandler();

        var auth = new SyncAuthenticationHandler(
            optionsAccessor,
            loggerFactory?.CreateLogger<SyncAuthenticationHandler>())
        {
            InnerHandler = primary,
        };

        var tracking = new TrackingHandler(loggerFactory?.CreateLogger<TrackingHandler>())
        {
            InnerHandler = auth,
        };

        HttpMessageHandler outermost = resiliencePipeline is null
            ? tracking
            : new ResilienceHandler(resiliencePipeline) { InnerHandler = tracking };

        var httpClient = new HttpClient(outermost)
        {
            BaseAddress = new Uri(options.GetBaseUrl(), UriKind.Absolute),
        };

        // Matches the DI path: HttpClient's 100-second ceiling covers the whole call, retries and backoff
        // included, so it is lifted only for the default pipeline - the one that bounds each attempt itself.
        // In every other case it is all that stops a black-holed connection from hanging the caller forever.
        if (pipelineBoundsAttempts)
        {
            httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        }

        return httpClient;
    }
}
