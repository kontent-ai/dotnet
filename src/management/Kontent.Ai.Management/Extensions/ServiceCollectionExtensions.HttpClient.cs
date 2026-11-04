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
    /// Configures the default resilience pipeline. Retries are idempotency-aware: HTTP 429 retries every request (it
    /// was rejected, not processed), while other transient failures — 408/5xx and transport exceptions, where the
    /// request may already have been applied — retry idempotent requests only. The HTTP method decides, except for the
    /// endpoints in <see cref="EndpointIdempotency"/>. Unlike Delivery and Sync there is no
    /// per-attempt timeout; <see cref="ManagementOptions.Timeout"/> bounds each request, retries included.
    /// </summary>
    internal static void ConfigureDefaultResilience(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        Action<HttpRetryStrategyOptions>? tuneRetry = null)
    {
        // No DelayGenerator override: the options' default (ShouldRetryAfterHeader) already honors a server-provided
        // Retry-After in both delta and HTTP-date form, falling back to the backoff below when absent.
        var retry = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, args.Context)),
        };
        tuneRetry?.Invoke(retry);
        builder.AddRetry(retry);
    }

    internal static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, ResilienceContext context)
    {
        if (outcome.Result is { } response)
        {
            return !response.IsSuccessStatusCode
                && (response.StatusCode == HttpStatusCode.TooManyRequests
                    || (HttpRetryPredicates.IsRetryableStatusCode(response.StatusCode)
                        && IsRequestIdempotent(response.RequestMessage ?? context.GetRequestMessage())));
        }

        return HttpRetryPredicates.IsTransientException(outcome.Exception, context.CancellationToken)
            && IsRequestIdempotent(context.GetRequestMessage());
    }

    internal static bool IsRequestIdempotent(HttpRequestMessage? request)
        => request is not null
        && (request.Options.TryGetValue(RefitMethodName, out var method) && EndpointIdempotency.TryGetValue(method, out var idempotent)
            ? idempotent
            : IsIdempotent(request.Method));

    private static readonly HttpRequestOptionsKey<string> RefitMethodName = new(HttpRequestMessageOptions.MethodName);

    // Verified against the live API: a second publish, new version, immediate unpublish or cancellation fails, while
    // filter, bulk get and validate only read.
    private static readonly Dictionary<string, bool> EndpointIdempotency = new()
    {
        [nameof(IManagementApi.PublishLanguageVariantInternalAsync)] = false,
        [nameof(IManagementApi.CreateNewVersionOfLanguageVariantInternalAsync)] = false,
        [nameof(IManagementApi.UnpublishLanguageVariantInternalAsync)] = false,
        [nameof(IManagementApi.CancelPublishingOfLanguageVariantInternalAsync)] = false,
        [nameof(IManagementApi.CancelUnpublishingOfLanguageVariantInternalAsync)] = false,
        [nameof(IManagementApi.FilterItemsWithVariantsInternalAsync)] = true,
        [nameof(IManagementApi.BulkGetItemsWithVariantsInternalAsync)] = true,
        [nameof(IManagementApi.ValidateEnvironmentInternalAsync)] = true,
    };

    // An unknown method — no request message available — is treated as non-idempotent: no retry unless provably safe.
    internal static bool IsIdempotent(HttpMethod? method)
        => method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;

}
