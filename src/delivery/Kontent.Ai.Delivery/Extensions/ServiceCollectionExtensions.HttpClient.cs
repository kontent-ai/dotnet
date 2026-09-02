using System.Text.Json;
using Kontent.Ai.Common.Clients;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Common;
using Kontent.Ai.Delivery.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery;

public static partial class ServiceCollectionExtensions
{
    private static TransportRecipe<DeliveryOptions> Transport(string name) => new(
        HttpClientName: GetHttpClientName(name),
        ResilienceHandlerName: $"delivery_{name}",
        // Read once, when the HTTP client is created; the authentication handler rewrites the host per
        // request, so a switched endpoint still takes effect.
        BaseAddress: options => new Uri(options.GetBaseUrl(), UriKind.Absolute),
        ResilienceEnabled: options => options.EnableResilience,
        Ceiling: (options, defaultPipeline, fallback) =>
            HttpClientTimeouts.Resolve(options.Timeout, options.EnableResilience && defaultPipeline, fallback),
        DefaultPipeline: DefaultResilience.ConfigureReadPipeline,
        AddHandlers: httpClient =>
        {
            // Registered after the resilience handler, so it re-runs on every retry attempt - see
            // FilterQueryHandler for why that requires it to be idempotent.
            httpClient.AddHttpMessageHandler(static () => new FilterQueryHandler());

            httpClient.AddHttpMessageHandler(sp => new TrackingHandler(
                sp.GetService<ILogger<TrackingHandler>>()));

            httpClient.AddHttpMessageHandler(sp => new DeliveryAuthenticationHandler(
                sp.GetRequiredKeyedService<IOptionsAccessor<DeliveryOptions>>(name),
                sp.GetService<ILogger<DeliveryAuthenticationHandler>>()));
        });

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
}
