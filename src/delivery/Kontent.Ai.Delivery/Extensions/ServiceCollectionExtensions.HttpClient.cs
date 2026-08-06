using System.Text.Json;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Delivery;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers and configures a named HTTP client with Refit.
    /// </summary>
    private static void RegisterNamedHttpClient(
        IServiceCollection services,
        string name,
        JsonSerializerOptions sharedJsonOptions,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        var refitSettings = CreateRefitSettings(sharedJsonOptions);

        var httpClientName = GetHttpClientName(name);
        var httpClientBuilder = services
            .AddHttpClient(httpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<DeliveryOptions>>();
                var options = optionsMonitor.Get(name);
                // Note: BaseAddress is static and won't update with runtime configuration changes.
                // The DeliveryAuthenticationHandler handles runtime endpoint switching.
                httpClient.BaseAddress = new Uri(options.GetBaseUrl(), UriKind.Absolute);
            });

        // Add resilience and message handlers
        ConfigureResilienceHandler(httpClientBuilder, $"delivery_{name}", name, configureResilience);
        AddMessageHandlers(httpClientBuilder, name);
        ConfigureConnectionRecycling(httpClientBuilder);

        // Apply custom configuration last, so a consumer can still replace anything set above.
        configureHttpClient?.Invoke(httpClientBuilder);

        // Register keyed IDeliveryApi - create Refit client from the configured HTTP pipeline
        services.AddKeyedTransient(name, (sp, _) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(httpClientName);
            return RestService.For<IDeliveryApi>(httpClient, refitSettings);
        });
    }

    /// <summary>
    /// Creates the Refit settings the Delivery API contract requires.
    /// </summary>
    /// <remarks>
    /// Not consumer-configurable: every value here is load-bearing. The key formatter is what turns
    /// POCO property names into the casing the API expects, and the serializer options carry the
    /// converters for the wire format.
    /// </remarks>
    private static RefitSettings CreateRefitSettings(JsonSerializerOptions sharedJsonOptions) =>
        new()
        {
            ContentSerializer = new SystemTextJsonContentSerializer(sharedJsonOptions),
            CollectionFormat = CollectionFormat.Multi,
            UrlParameterKeyFormatter = new CamelCaseUrlParameterKeyFormatter()
        };

    /// <summary>
    /// Configures the resilience handler for an HTTP client.
    /// </summary>
    /// <param name="httpClientBuilder">The HTTP client builder.</param>
    /// <param name="resilienceHandlerName">The name of the resilience handler.</param>
    /// <param name="clientName">The name of the named client options.</param>
    /// <param name="configureResilience">Optional custom resilience configuration.</param>
    private static void ConfigureResilienceHandler(
        IHttpClientBuilder httpClientBuilder,
        string resilienceHandlerName,
        string clientName,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        httpClientBuilder.AddResilienceHandler(resilienceHandlerName, (builder, context) =>
        {
            var optionsMonitor = context.ServiceProvider.GetRequiredService<IOptionsMonitor<DeliveryOptions>>();
            var options = optionsMonitor.Get(clientName);

            if (!options.EnableResilience)
                return;

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

    /// <summary>
    /// Adds tracking and authentication message handlers to an HTTP client.
    /// </summary>
    /// <param name="httpClientBuilder">The HTTP client builder.</param>
    /// <param name="clientName">The name of the options for the authentication handler.</param>
    private static void AddMessageHandlers(IHttpClientBuilder httpClientBuilder, string clientName)
    {
        // Registered after the resilience handler, so it re-runs on every retry attempt - see
        // FilterQueryHandler for why that requires it to be idempotent.
        httpClientBuilder.AddHttpMessageHandler(static () => new FilterQueryHandler());

        httpClientBuilder.AddHttpMessageHandler(sp => new TrackingHandler(
            sp.GetService<ILogger<TrackingHandler>>()));

        httpClientBuilder.AddHttpMessageHandler(sp => new DeliveryAuthenticationHandler(
            sp.GetRequiredKeyedService<IDeliveryOptionsAccessor>(clientName),
            sp.GetService<ILogger<DeliveryAuthenticationHandler>>()));
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

    private static void ConfigureDefaultResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        // Retry policy with Retry-After header support
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                HttpRetryPredicates.IsTransientException(args.Outcome.Exception, args.Context.CancellationToken) ||
                (args.Outcome.Result?.IsSuccessStatusCode == false &&
                 HttpRetryPredicates.IsRetryableStatusCode(args.Outcome.Result?.StatusCode))),
            DelayGenerator = HttpRetryDelay.FromRetryAfterHeader
        });

        builder.AddTimeout(TimeSpan.FromSeconds(30));
    }
}
