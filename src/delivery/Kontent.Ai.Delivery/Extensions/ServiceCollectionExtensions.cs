using System.Text.Json;
using Kontent.Ai.Common;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Extension methods for registering Kontent.ai Delivery SDK services.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    private const string HttpClientNamePrefix = "Kontent.Ai.Delivery.HttpClient.";

    /// <summary>
    /// Registers the Kontent.ai Delivery client with the specified options instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="deliveryOptions">The delivery options instance.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        DeliveryOptions deliveryOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(deliveryOptions);

        return services.AddDeliveryClient(
            NamedClients.Default,
            deliveryOptions.CopyTo,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client with the specified options builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="buildDeliveryOptions">A function to build the delivery options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        Func<IDeliveryOptionsBuilder, DeliveryOptions> buildDeliveryOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(buildDeliveryOptions);

        var builder = DeliveryOptionsBuilder.CreateInstance();
        var options = buildDeliveryOptions(builder);

        return services.AddDeliveryClient(
            NamedClients.Default,
            options.CopyTo,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="configurationSectionName">The configuration section name. Defaults to "DeliveryOptions".</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = DeliveryOptions.DefaultConfigurationSectionName,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
        => services.AddDeliveryClient(
            NamedClients.Default,
            configuration,
            configurationSectionName,
            configureHttpClient,
            configureResilience);

    /// <summary>
    /// Registers a named Kontent.ai Delivery client using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="configurationSectionName">The configuration section name. Defaults to "DeliveryOptions".</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string configurationSectionName = DeliveryOptions.DefaultConfigurationSectionName,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddDeliveryClientFromConfiguration(
            name,
            section,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client using a configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSection">The configuration section containing delivery options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
        => services.AddDeliveryClient(
            NamedClients.Default,
            configurationSection,
            configureHttpClient,
            configureResilience);

    /// <summary>
    /// Registers a named Kontent.ai Delivery client using a configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configurationSection">The configuration section containing delivery options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        string name,
        IConfigurationSection configurationSection,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddDeliveryClientFromConfiguration(
            name,
            configurationSection,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client with configuration action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the delivery options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        Action<DeliveryOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.AddDeliveryClient(
            NamedClients.Default,
            configureOptions);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client with a configuration action that can resolve services from the container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the delivery options with access to the <see cref="IServiceProvider"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this overload when the options need to read values from other services registered in the container,
    /// e.g. <c>sp.GetRequiredService&lt;IOptions&lt;SiteOptions&gt;&gt;().Value</c>.
    /// </para>
    /// <para>
    /// <b>Avoid circular dependencies:</b> the callback must not resolve <c>IDeliveryClient</c>, <c>IDeliveryApi</c>, or any
    /// service that transitively depends on them — doing so will recurse through options resolution when the client is built.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        Action<IServiceProvider, DeliveryOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.AddDeliveryClient(
            NamedClients.Default,
            configureOptions);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client with advanced configuration options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the delivery options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        Action<DeliveryOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        return services.AddDeliveryClient(
            NamedClients.Default,
            configureOptions,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Delivery client with advanced configuration options and access to the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the delivery options with access to the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        Action<IServiceProvider, DeliveryOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        return services.AddDeliveryClient(
            NamedClients.Default,
            configureOptions,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers a named Kontent.ai Delivery client with the specified configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registered client can be accessed in two ways:
    /// <list type="bullet">
    /// <item>Via <see cref="IDeliveryClientFactory"/>: <c>factory.Get("name")</c></item>
    /// <item>Via keyed services injection: <c>[FromKeyedServices("name")] IDeliveryClient client</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// The client supports reactive configuration updates via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>.
    /// Changes to API keys and other options will be picked up automatically at runtime.
    /// </para>
    /// <para>
    /// Note: The HTTP client's BaseAddress and resilience pipeline (including <see cref="DeliveryOptions.EnableResilience"/>)
    /// are set once during initialization and will not update with runtime configuration changes.
    /// However, the authentication handler monitors options changes to support scenarios like
    /// API key rotation and endpoint switching.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the delivery options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the same name is already registered.</exception>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        string name,
        Action<DeliveryOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.AddDeliveryClient(
            name,
            (_, opts) => configureOptions(opts),
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers a named Kontent.ai Delivery client with a configuration action that can resolve services from the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this overload when the options need to read values from other services registered in the container.
    /// The callback is invoked when <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> is first resolved,
    /// allowing composition with sibling options such as <c>IOptions&lt;SiteOptions&gt;</c>.
    /// </para>
    /// <para>
    /// See the <see cref="AddDeliveryClient(IServiceCollection, string, Action{DeliveryOptions}, Action{IHttpClientBuilder}?, Action{Polly.ResiliencePipelineBuilder{HttpResponseMessage}}?)"/>
    /// overload for registration semantics (keyed services, factory access, options monitoring).
    /// </para>
    /// <para>
    /// <b>Avoid circular dependencies:</b> the callback must not resolve <c>IDeliveryClient</c>, <c>IDeliveryApi</c>, or any
    /// service that transitively depends on them — doing so will recurse through options resolution when the client is built.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the delivery options with access to the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the same name is already registered.</exception>
    public static IServiceCollection AddDeliveryClient(
        this IServiceCollection services,
        string name,
        Action<IServiceProvider, DeliveryOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        KeyedClients.EnsureNotRegistered<IDeliveryClient>(services, name, "delivery client", GetHttpClientName(name));

        OptionsRegistration.RegisterValidated<DeliveryOptions>(services, name, builder =>
            builder.Configure<IServiceProvider>((opts, sp) => configureOptions(sp, opts)));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    private static IServiceCollection AddDeliveryClientFromConfiguration(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configuration);

        KeyedClients.EnsureNotRegistered<IDeliveryClient>(services, name, "delivery client", GetHttpClientName(name));

        OptionsRegistration.RegisterValidated<DeliveryOptions>(services, name, builder => builder.Bind(configuration));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    private static IServiceCollection CompleteClientRegistration(
        IServiceCollection services,
        string name,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<Polly.ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        // Create shared JSON options once and use for both DI and Refit (avoids two divergent instances)
        var sharedJsonOptions = GetOrCreateSharedJsonOptions(services);

        // Register dependencies (only once)
        RegisterDependencies(services, sharedJsonOptions);

        // Per-client options accessor — bridges named IOptionsMonitor reads to the rest of the SDK.
        services.AddKeyedSingleton<IOptionsAccessor<DeliveryOptions>>(name, (sp, _) =>
            new MonitorBackedOptionsAccessor<DeliveryOptions>(sp.GetRequiredService<IOptionsMonitor<DeliveryOptions>>(), name));

        // Register named HTTP client and Refit API
        RegisterNamedHttpClient(services, name, sharedJsonOptions, configureHttpClient, configureResilience);

        // Register keyed IDeliveryClient
        services.AddKeyedSingleton<IDeliveryClient>(name, CreateDeliveryClient);

        // Register factory
        services.TryAddSingleton<IDeliveryClientFactory, DeliveryClientFactory>();

        // Register default client accessors if this is the default name (backward compatibility)
        if (name == NamedClients.Default)
        {
            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<IDeliveryApi>(NamedClients.Default));

            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<IDeliveryClient>(NamedClients.Default));
        }

        return services;
    }

    /// <summary>
    /// Factory method for creating keyed DeliveryClient instances.
    /// </summary>
    private static IDeliveryClient CreateDeliveryClient(IServiceProvider sp, object? key)
        => CreateDeliveryClient(sp, (string)key!);

    /// <summary>
    /// Builds a client from services already registered in <paramref name="sp"/>.
    /// </summary>
    /// <param name="sp">The provider to resolve dependencies from.</param>
    /// <param name="clientName">The name the client's services were registered under.</param>
    /// <param name="ownedResources">
    /// Handed to the client as its own to dispose. The container passes nothing - it owns what it built.
    /// <see cref="DeliveryClientBuilder"/> passes the provider itself, which is what makes the client it
    /// returns disposable. Both entry points construct through here so they cannot drift apart.
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

        // Resolve keyed cache manager for this client (registered via AddDeliveryMemoryCache/AddDeliveryHybridCache/AddDeliveryCacheManager)
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
