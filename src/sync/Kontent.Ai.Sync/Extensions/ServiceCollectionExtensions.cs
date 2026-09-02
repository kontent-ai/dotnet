using Kontent.Ai.Common.Http;
using Kontent.Ai.Common;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Sync;

/// <summary>
/// Extension methods for registering Kontent.ai Sync SDK services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string HttpClientNamePrefix = "Kontent.Ai.Sync.HttpClient.";

    /// <summary>
    /// Registers the Kontent.ai Sync client with the specified options instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="syncOptions">The sync options instance.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        SyncOptions syncOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(syncOptions);

        return services.AddSyncClient(
            NamedClients.Default,
            options => OptionsCopier<SyncOptions>.Copy(syncOptions, options),
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client with the specified options builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="buildSyncOptions">A function to build the sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        Func<ISyncOptionsBuilder, SyncOptions> buildSyncOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(buildSyncOptions);

        var builder = SyncOptionsBuilder.CreateInstance();
        var options = buildSyncOptions(builder);

        return services.AddSyncClient(
            NamedClients.Default,
            opts => OptionsCopier<SyncOptions>.Copy(options, opts),
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="configurationSectionName">The configuration section name. Defaults to "SyncOptions".</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = SyncOptions.DefaultConfigurationSectionName,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
        => services.AddSyncClient(
            NamedClients.Default,
            configuration,
            configurationSectionName,
            configureHttpClient,
            configureResilience);

    /// <summary>
    /// Registers a named Kontent.ai Sync client using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="configurationSectionName">The configuration section name. Defaults to "SyncOptions".</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string configurationSectionName = SyncOptions.DefaultConfigurationSectionName,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddSyncClientFromConfiguration(name, section, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client using a configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSection">The configuration section containing sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
        => services.AddSyncClient(
            NamedClients.Default,
            configurationSection,
            configureHttpClient,
            configureResilience);

    /// <summary>
    /// Registers a named Kontent.ai Sync client using a configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configurationSection">The configuration section containing sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        IConfigurationSection configurationSection,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddSyncClientFromConfiguration(name, configurationSection, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client with configuration action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the sync options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        Action<SyncOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.AddSyncClient(NamedClients.Default, configureOptions);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client with advanced configuration options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        Action<SyncOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        return services.AddSyncClient(
            NamedClients.Default,
            configureOptions,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers a named Kontent.ai Sync client with the specified configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the same name is already registered.</exception>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        Action<SyncOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        KeyedClients.EnsureNotRegistered<ISyncClient>(services, name, "sync client", GetHttpClientName(name));

        OptionsRegistration.RegisterValidated<SyncOptions>(services, name, builder => builder.Configure(configureOptions));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client, configuring options with access to the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <remarks>
    /// Use when the options depend on something else in the container - a secret store, a tenant resolver.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the options with access to the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        Action<IServiceProvider, SyncOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
        => services.AddSyncClient(NamedClients.Default, configureOptions, configureHttpClient, configureResilience);

    /// <summary>
    /// Registers a named Kontent.ai Sync client, configuring options with access to the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the options with access to the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the same name is already registered.</exception>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        Action<IServiceProvider, SyncOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        KeyedClients.EnsureNotRegistered<ISyncClient>(services, name, "sync client", GetHttpClientName(name));

        OptionsRegistration.RegisterValidated<SyncOptions>(services, name, builder =>
            builder.Configure<IServiceProvider>((opts, sp) => configureOptions(sp, opts)));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    private static IServiceCollection AddSyncClientFromConfiguration(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configuration);

        KeyedClients.EnsureNotRegistered<ISyncClient>(services, name, "sync client", GetHttpClientName(name));

        OptionsRegistration.RegisterValidated<SyncOptions>(services, name, builder => builder.Bind(configuration));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    private static IServiceCollection CompleteClientRegistration(
        IServiceCollection services,
        string name,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        RegisterNamedHttpClient(services, name, configureHttpClient, configureResilience);

        services.AddKeyedSingleton<ISyncClient>(name, CreateSyncClient);
        services.TryAddSingleton<ISyncClientFactory, SyncClientFactory>();

        if (name == NamedClients.Default)
        {
            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<ISyncClient>(NamedClients.Default));
        }

        return services;
    }

    private static ISyncClient CreateSyncClient(IServiceProvider serviceProvider, object? key)
    {
        var clientName = (string)key!;
        var syncApi = serviceProvider.GetRequiredKeyedService<ISyncApi>(clientName);
        var optionsAccessor = new MonitorBackedOptionsAccessor<SyncOptions>(
            serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>(),
            clientName);

        return new SyncClient(syncApi, optionsAccessor);
    }

    private static string GetHttpClientName(string name) => $"{HttpClientNamePrefix}{name}";

    /// <summary>
    /// Registers and configures a named HTTP client with Refit.
    /// </summary>
    private static void RegisterNamedHttpClient(
        IServiceCollection services,
        string name,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        var refitSettings = RefitSettingsProvider.CreateDefaultSettings();

        var httpClientName = GetHttpClientName(name);
        var httpClientBuilder = services
            .AddKeyedRefitGeneratedClient<ISyncApi>(name, refitSettings, httpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
                var options = optionsMonitor.Get(name);
                httpClient.BaseAddress = new Uri(options.GetBaseUrl(), UriKind.Absolute);

                httpClient.Timeout = HttpClientTimeouts.Resolve(
                    options.Timeout,
                    defaultPipelineBoundsAttempts: options.EnableResilience && configureResilience is null,
                    httpClient.Timeout);
            });

        ResilienceHandlers.AddOptionsGated<SyncOptions>(
            httpClientBuilder,
            $"sync_{name}",
            name,
            options => options.EnableResilience,
            configureResilience,
            DefaultResilience.ConfigureReadPipeline);
        AddMessageHandlers(httpClientBuilder, name);
        HttpClientDefaults.ConfigureConnectionRecycling(httpClientBuilder);
        // Applied last, so a consumer can still replace anything set above.
        configureHttpClient?.Invoke(httpClientBuilder);
    }

    /// <summary>
    /// Builds the client <see cref="SyncClientBuilder"/> returns: the same registration the container
    /// path runs, drawn from a provider the builder assembled, with the client owning what it drew and
    /// the provider itself. Takes ownership of <paramref name="provider"/> even when construction fails.
    /// </summary>
    internal static SyncClient CreateOwnedSyncClient(ServiceProvider provider, string name)
    {
        try
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GetHttpClientName(name));
            var syncApi = RestService.For<ISyncApi>(httpClient, RefitSettingsProvider.CreateDefaultSettings());
            var optionsAccessor = new MonitorBackedOptionsAccessor<SyncOptions>(
                provider.GetRequiredService<IOptionsMonitor<SyncOptions>>(),
                name);

            // The HttpClient before the provider: a request after disposal then fails at once, whatever
            // the factory-owned handler behind it is still doing.
            return new SyncClient(syncApi, optionsAccessor, new CompositeDisposable(httpClient, provider));
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds tracking and authentication message handlers to an HTTP client.
    /// </summary>
    private static void AddMessageHandlers(IHttpClientBuilder httpClientBuilder, string clientName)
    {
        httpClientBuilder.AddHttpMessageHandler(sp => new TrackingHandler(
            sp.GetService<ILogger<TrackingHandler>>()));

        httpClientBuilder.AddHttpMessageHandler(sp => new SyncAuthenticationHandler(
            new MonitorBackedOptionsAccessor<SyncOptions>(sp.GetRequiredService<IOptionsMonitor<SyncOptions>>(), clientName),
            sp.GetService<ILogger<SyncAuthenticationHandler>>()));
    }
}
