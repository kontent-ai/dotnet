using System.Text.Json;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Common;
using Kontent.Ai.Delivery.Handlers;
using Microsoft.Extensions.DependencyInjection;
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
            .AddKeyedRefitGeneratedClient<IDeliveryApi>(name, refitSettings, httpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<DeliveryOptions>>();
                var options = optionsMonitor.Get(name);
                // Note: BaseAddress is static and won't update with runtime configuration changes.
                // The DeliveryAuthenticationHandler handles runtime endpoint switching.
                httpClient.BaseAddress = new Uri(options.GetBaseUrl(), UriKind.Absolute);

                httpClient.Timeout = HttpClientTimeouts.Resolve(
                    options.Timeout,
                    defaultPipelineBoundsAttempts: options.EnableResilience && configureResilience is null,
                    httpClient.Timeout);
            });

        ResilienceHandlers.AddOptionsGated<DeliveryOptions>(
            httpClientBuilder,
            $"delivery_{name}",
            name,
            options => options.EnableResilience,
            configureResilience,
            DefaultResilience.ConfigureReadPipeline);
        AddMessageHandlers(httpClientBuilder, name);
        HttpClientDefaults.ConfigureConnectionRecycling(httpClientBuilder);

        // Apply custom configuration last, so a consumer can still replace anything set above.
        configureHttpClient?.Invoke(httpClientBuilder);
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
            sp.GetRequiredKeyedService<IOptionsAccessor<DeliveryOptions>>(clientName),
            sp.GetService<ILogger<DeliveryAuthenticationHandler>>()));
    }
}
