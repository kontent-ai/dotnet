using System.Text.Json;
using Kontent.Ai.Common;
using Kontent.Ai.Common.Clients;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Registers Kontent.ai Delivery clients.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    private const string HttpClientNamePrefix = "Kontent.Ai.Delivery.HttpClient.";

    /// <summary>
    /// Registers the default Delivery client.
    /// </summary>
    /// <remarks>
    /// The client's options are also the unnamed <see cref="IOptions{TOptions}"/> / <see cref="IOptionsMonitor{TOptions}"/>
    /// of <see cref="DeliveryOptions"/>: a copy of the named ones that follows their reloads. Configuring the unnamed
    /// options directly does not reach the client - a <c>services.Configure&lt;DeliveryOptions&gt;(...)</c> registered
    /// before this call is overwritten by the copy, and one registered after it changes only the copy.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client: its options, HTTP client, resilience, caching.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(this IServiceCollection services, Action<IDeliveryClientBuilder> configure)
        => services.AddDeliveryClient(NamedClients.Default, configure);

    /// <summary>
    /// Registers the default Delivery client from a pre-built options instance.
    /// </summary>
    /// <remarks>
    /// The instance's values are copied onto the options the container materializes, when those are first
    /// built; the object itself is not registered. A change made to the instance before that point is
    /// included, one made after it is not.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The options to copy.</param>
    /// <param name="configure">Configures the client further, after the options are copied.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(this IServiceCollection services, DeliveryOptions options, Action<IDeliveryClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return services.AddDeliveryClient(NamedClients.Default, delivery =>
        {
            delivery.Options.Configure(options.CopyTo);
            configure?.Invoke(delivery);
        });
    }

    /// <summary>
    /// Registers a named Delivery client, resolvable through <see cref="IDeliveryClientFactory"/> or as a
    /// keyed service under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options are read per request through <see cref="IOptionsMonitor{TOptions}"/>, so a reloaded API
    /// key or a switched endpoint takes effect without rebuilding the client. The resilience pipeline is
    /// read once, when the HTTP client is first created.
    /// </para>
    /// <para>
    /// A callback that resolves services - <c>Options.Configure&lt;IServiceProvider&gt;</c> - must not resolve
    /// <c>IDeliveryClient</c>, <c>IOptions&lt;DeliveryOptions&gt;</c> or anything that depends on them: doing
    /// so re-enters the options factory, and the container recurses without bound.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name. Must be unique across all registrations.</param>
    /// <param name="configure">Configures the client: its options, HTTP client, resilience, caching.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">A client with the same name is already registered.</exception>
    public static IServiceCollection AddDeliveryClient(this IServiceCollection services, string name, Action<IDeliveryClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = ClientRegistration.AddClient<DeliveryOptions, IDeliveryClient, DeliveryClientBuilder>(
            services,
            name,
            "delivery client",
            GetHttpClientName(name),
            static (name, services, options) => new DeliveryClientBuilder(name, services, options));

        // Create shared JSON options once and use for both DI and Refit (avoids two divergent instances)
        var sharedJsonOptions = GetOrCreateSharedJsonOptions(services);
        RegisterDependencies(services, sharedJsonOptions);

        // Per-client options accessor — bridges named IOptionsMonitor reads to the rest of the SDK.
        services.AddKeyedSingleton<IOptionsAccessor<DeliveryOptions>>(name, (sp, _) =>
            new MonitorBackedOptionsAccessor<DeliveryOptions>(sp.GetRequiredService<IOptionsMonitor<DeliveryOptions>>(), name));

        builder.HttpClient = ClientRegistration.AddTransport<DeliveryOptions, IDeliveryApi>(
            builder,
            Transport(name),
            CreateRefitSettings(sharedJsonOptions));

        ClientRegistration.AddClientServices<IDeliveryClient, IDeliveryClientFactory, DeliveryClientFactory>(services, name, CreateDeliveryClient);

        if (name == NamedClients.Default)
        {
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IDeliveryApi>(NamedClients.Default));
        }

        configure(builder);
        return services;
    }

    private static IDeliveryClient CreateDeliveryClient(IServiceProvider sp, object? key)
        => CreateDeliveryClient(sp, (string)key!);

    /// <summary>
    /// Builds a client from services already registered in <paramref name="sp"/>.
    /// </summary>
    /// <param name="sp">The provider to resolve dependencies from.</param>
    /// <param name="clientName">The name the client's services were registered under.</param>
    /// <param name="ownedResources">
    /// Handed to the client as its own to dispose. The container passes nothing - it owns what it built.
    /// <see cref="DeliveryClient.Create(Action{IDeliveryClientBuilder})"/> passes the provider itself, which
    /// is what makes the client it returns disposable. Both entry points construct through here so they
    /// cannot drift apart.
    /// </param>
    internal static DeliveryClient CreateDeliveryClient(
        IServiceProvider sp,
        string clientName,
        IDisposable? ownedResources = null)
    {
        var deliveryApi = sp.GetRequiredKeyedService<IDeliveryApi>(clientName);
        var contentItemMapper = sp.GetRequiredService<ContentItemMapper>();
        var contentDeserializer = sp.GetRequiredService<ContentDeserializer>();
        var typeProvider = sp.GetRequiredService<ITypeProvider>();
        var optionsAccessor = sp.GetRequiredKeyedService<IOptionsAccessor<DeliveryOptions>>(clientName);

        // Resolve keyed cache manager for this client (registered via the caching package's Use… methods)
        var cacheManager = sp.GetKeyedService<IDeliveryCacheManager>(clientName);

        // Resolve logger (optional - will be null if no logging is configured)
        var logger = sp.GetService<ILogger<DeliveryClient>>();

        return new DeliveryClient(
            deliveryApi,
            contentItemMapper,
            contentDeserializer,
            typeProvider,
            cacheManager,
            logger,
            optionsAccessor,
            ownedResources);
    }

    /// <summary>
    /// Returns the options a previously registered client already shares, or creates the SDK's own, so
    /// that Refit and the internal mappers read the wire through one instance.
    /// </summary>
    /// <remarks>
    /// Keyed on <see cref="DeliveryJsonOptions"/> rather than on <see cref="JsonSerializerOptions"/>: the
    /// bare type belongs to the application, and adopting whatever it had registered there gave the SDK a
    /// serializer without its own converters - see <see cref="DeliveryJsonOptions"/>.
    /// </remarks>
    private static JsonSerializerOptions GetOrCreateSharedJsonOptions(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(DeliveryJsonOptions));

        return existing?.ImplementationInstance is DeliveryJsonOptions registered
            ? registered.Value
            : RefitSettingsProvider.CreateDefaultJsonSerializerOptions();
    }

    private static string GetHttpClientName(string name) => $"{HttpClientNamePrefix}{name}";
}
