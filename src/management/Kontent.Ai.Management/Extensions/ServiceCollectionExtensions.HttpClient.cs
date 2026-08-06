using Kontent.Ai.Common.Http;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Handlers;
using Kontent.Ai.Management.Models.Assets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using System.Net;

namespace Kontent.Ai.Management.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static void RegisterRefitClient<T>(
        IServiceCollection services,
        string clientName,
        string httpClientName,
        Func<ManagementOptions, string> scopePathSelector,
        RefitSettings refitSettings,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience) where T : class
    {
        var httpClientBuilder = services
            .AddHttpClient(httpClientName)
            .ConfigureHttpClient((sp, httpClient) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<ManagementOptions>>().Get(clientName);
                httpClient.BaseAddress = options.ScopedEndpoint(scopePathSelector(options));
                httpClient.Timeout = options.Timeout;
            });

        // Resilience first → resilience sits outermost so each retry re-runs tracking + auth fresh (matters when
        // tokens rotate). Diverges from delivery/sync by omitting AddTimeout — see ConfigureDefaultResilience.
        ConfigureResilienceHandler(httpClientBuilder, $"management_{typeof(T).Name}_{clientName}", clientName, configureResilience);
        AddMessageHandlers(httpClientBuilder, clientName);
        ConfigureConnectionRecycling(httpClientBuilder);
        // Applied last, so a consumer can still replace anything set above.
        configureHttpClient?.Invoke(httpClientBuilder);

        services.AddKeyedTransient<T>(clientName, (sp, _) =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
            return RestService.For<T>(httpClient, refitSettings);
        });
    }

    private static RefitSettings CreateRefitSettings()
    {
        var settings = RefitSettingsProvider.CreateDefaultSettings();
        return settings;
    }

    /// <summary>
    /// Gives the client's connections a bounded lifetime so DNS changes are picked up.
    /// </summary>
    /// <remarks>
    /// The client is a keyed singleton and resolves its <see cref="HttpClient"/> once, so the handler
    /// chain it holds is never rotated - <see cref="IHttpClientFactory"/> only hands a fresh chain to a
    /// *new* <c>CreateClient</c> call. Without this, a long-running application keeps talking to whatever
    /// address it resolved at startup, indefinitely. Two minutes matches the factory's own default
    /// handler lifetime, so a connection lives no longer than it would on the non-singleton path.
    /// </remarks>
    private static void ConfigureConnectionRecycling(IHttpClientBuilder httpClientBuilder) =>
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });

    private static void ConfigureResilienceHandler(
        IHttpClientBuilder httpClientBuilder,
        string resilienceHandlerName,
        string clientName,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        httpClientBuilder.AddResilienceHandler(resilienceHandlerName, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptionsMonitor<ManagementOptions>>().Get(clientName);

            if (!options.EnableResilience)
            {
                return;
            }

            if (configureResilience is not null)
            {
                configureResilience(builder);
            }
            else
            {
                ConfigureDefaultResilience(builder);
            }
        });
    }

    private static void AddMessageHandlers(IHttpClientBuilder httpClientBuilder, string clientName)
    {
        httpClientBuilder.AddHttpMessageHandler(_ => new TrackingHandler());
        httpClientBuilder.AddHttpMessageHandler(sp => new ManagementAuthenticationHandler(
            new MonitorBackedManagementOptionsAccessor(
                sp.GetRequiredService<IOptionsMonitor<ManagementOptions>>(),
                clientName)));
    }

    /// <summary>
    /// Configures the default resilience pipeline. Retries are idempotency-aware: HTTP 429 retries for every method
    /// (the request was rejected, not processed), while other transient failures — 408/5xx and transport exceptions,
    /// where the request may have already been applied server-side — retry only for idempotent methods. POST and
    /// PATCH are never assumed safe: a replayed create can duplicate an entity, and the Management API PATCH grammar
    /// includes non-idempotent <c>addInto</c>. Consumers who want different semantics (e.g. retrying the POST-based
    /// variant-filter listing) replace the pipeline via the <c>configureResilience</c> hook. Diverges from
    /// delivery-sdk-net / sync-sdk-net by omitting <c>AddTimeout</c> — management operations include asset uploads
    /// where a per-attempt timeout would be more hindrance than help. The ceiling on the call as a whole is
    /// <see cref="ManagementOptions.Timeout"/>.
    /// </summary>
    internal static void ConfigureDefaultResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        // No DelayGenerator override: the options' default (ShouldRetryAfterHeader) already honors a server-provided
        // Retry-After in both delta and HTTP-date form, falling back to the backoff below when absent.
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, args.Context)),
        });
    }

    internal static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, ResilienceContext context)
    {
        // A body that cannot be produced twice makes the request unsafe to re-send whatever the status was:
        // the second attempt would upload an empty file, and the API can accept it. Returning the original
        // failure instead leaves the decision with the caller, who is the only one able to rewind the source.
        if (!IsReplayable(outcome.Result?.RequestMessage ?? context.GetRequestMessage()))
        {
            return false;
        }

        if (outcome.Result is { } response)
        {
            return !response.IsSuccessStatusCode
                && (response.StatusCode == HttpStatusCode.TooManyRequests
                    || (HttpRetryPredicates.IsRetryableStatusCode(response.StatusCode) && IsIdempotent(RequestMethod(response, context))));
        }

        return HttpRetryPredicates.IsTransientException(outcome.Exception, context.CancellationToken)
            && IsIdempotent(context.GetRequestMessage()?.Method);
    }

    // The response usually carries its request (set by the primary handler); the resilience context (populated by
    // ResilienceHandler) is the fallback for handlers that don't set it.
    private static HttpMethod? RequestMethod(HttpResponseMessage response, ResilienceContext context)
        => (response.RequestMessage ?? context.GetRequestMessage())?.Method;

    private static bool IsReplayable(HttpRequestMessage? request)
        => request?.Content is not FileUploadContent upload || upload.IsReplayable;

    // An unknown method — no request message available — is treated as non-idempotent: no retry unless provably safe.
    internal static bool IsIdempotent(HttpMethod? method)
        => method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;

}
