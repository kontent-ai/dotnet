using System.Net;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Common;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

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
        HttpClientDefaults.ConfigureConnectionRecycling(httpClientBuilder);
        // Applied last, so a consumer can still replace anything set above.
        configureHttpClient?.Invoke(httpClientBuilder);

        services.AddKeyedTransient<T>(clientName, (sp, _) =>
            CreateApi<T>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName), refitSettings));
    }

    private static T CreateApi<T>(HttpClient httpClient, RefitSettings refitSettings) where T : class
        => RestService.For<T>(httpClient, refitSettings);

    /// <summary>
    /// Builds the API clients a standalone <see cref="ManagementClient"/> owns: the same registration the
    /// container path runs, drawn from a provider the caller assembled. Each scope's <see cref="HttpClient"/>
    /// is created only when its identifier is configured, as the container path resolves them. Takes
    /// ownership of <paramref name="provider"/> even when construction fails.
    /// </summary>
    internal static (IManagementApi? Api, ISubscriptionApi? SubscriptionApi, IDisposable OwnedResources) CreateOwnedApis(
        ServiceProvider provider,
        string name)
    {
        try
        {
            var options = provider.GetRequiredService<IOptionsMonitor<ManagementOptions>>().Get(name);
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var refitSettings = RefitSettingsProvider.CreateDefaultSettings();
            var owned = new List<IDisposable>(3);

            IManagementApi? api = null;
            if (options.HasEnvironmentId())
            {
                var httpClient = httpClientFactory.CreateClient(EnvironmentHttpClientName(name));
                owned.Add(httpClient);
                api = CreateApi<IManagementApi>(httpClient, refitSettings);
            }

            ISubscriptionApi? subscriptionApi = null;
            if (options.HasSubscriptionId())
            {
                var httpClient = httpClientFactory.CreateClient(SubscriptionHttpClientName(name));
                owned.Add(httpClient);
                subscriptionApi = CreateApi<ISubscriptionApi>(httpClient, refitSettings);
            }

            // The HttpClients before the provider: a request after disposal then fails at once, whatever
            // the factory-owned handlers behind them are still doing.
            owned.Add(provider);
            return (api, subscriptionApi, new CompositeDisposable([.. owned]));
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

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
            new MonitorBackedOptionsAccessor<ManagementOptions>(
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

    // An unknown method — no request message available — is treated as non-idempotent: no retry unless provably safe.
    internal static bool IsIdempotent(HttpMethod? method)
        => method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;

}
