using System.Net;
using Kontent.Ai.Common.Clients;
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
    /// <summary>
    /// One scope's transport. Diverges from Delivery and Sync in two documented ways: the ceiling is always the
    /// configured <see cref="ManagementOptions.Timeout"/>, since no per-attempt timeout is installed, and the
    /// default pipeline is the idempotency-aware one below.
    /// </summary>
    private static TransportRecipe<ManagementOptions> Transport<T>(string httpClientName, string clientName, Func<ManagementOptions, string> scopePath) => new(
        HttpClientName: httpClientName,
        ResilienceHandlerName: $"management_{typeof(T).Name}_{clientName}",
        BaseAddress: options => options.ScopedEndpoint(scopePath(options)),
        ResilienceEnabled: options => options.EnableResilience,
        Ceiling: (options, _, _) => options.Timeout,
        DefaultPipeline: ConfigureDefaultResilience,
        AddHandlers: httpClient =>
        {
            httpClient.AddHttpMessageHandler(_ => new TrackingHandler());
            httpClient.AddHttpMessageHandler(sp => new ManagementAuthenticationHandler(
                new MonitorBackedOptionsAccessor<ManagementOptions>(
                    sp.GetRequiredService<IOptionsMonitor<ManagementOptions>>(),
                    clientName)));
        });

    /// <summary>
    /// Resolves the API clients a standalone <see cref="ManagementClient"/> uses from a provider the caller
    /// assembled - the same keyed clients the container path resolves - and hands the provider back as the
    /// resource the client owns. Takes ownership of <paramref name="provider"/> even when construction fails.
    /// </summary>
    internal static (IManagementApi? Api, ISubscriptionApi? SubscriptionApi, IDisposable OwnedResources) CreateOwnedApis(
        ServiceProvider provider,
        string name)
    {
        try
        {
            var (api, subscriptionApi) = ResolveApis(provider, name);
            return (api, subscriptionApi, provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Configures the default resilience pipeline. Retries are idempotency-aware: HTTP 429 retries for every method
    /// (the request was rejected, not processed), while other transient failures — 408/5xx and transport exceptions,
    /// where the request may have already been applied server-side — retry only for idempotent methods. POST and
    /// PATCH are never assumed safe: a replayed create can duplicate an entity, and the Management API PATCH grammar
    /// includes non-idempotent <c>addInto</c>. Consumers who want different semantics (e.g. retrying the POST-based
    /// variant-filter listing) replace the pipeline through <c>ConfigureResilience</c> on the builder, which applies to both transports. Diverges from
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
