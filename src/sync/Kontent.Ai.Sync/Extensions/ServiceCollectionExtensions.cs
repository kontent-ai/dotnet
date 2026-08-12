using Kontent.Ai.Common;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
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

        EnsureClientNameNotAlreadyRegistered(services, name);

        services.Configure(name, configureOptions);
        services.AddOptions<SyncOptions>(name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The default client's options are also registered unnamed, so IOptions<SyncOptions> resolves
        // without a name.
        if (name == NamedClients.Default)
        {
            services.Configure(configureOptions);
            services.AddOptions<SyncOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

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

        EnsureClientNameNotAlreadyRegistered(services, name);

        services.AddOptions<SyncOptions>(name)
            .Configure<IServiceProvider>((opts, sp) => configureOptions(sp, opts))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (name == NamedClients.Default)
        {
            services.AddOptions<SyncOptions>()
                .Configure<IServiceProvider>((opts, sp) => configureOptions(sp, opts))
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

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

        EnsureClientNameNotAlreadyRegistered(services, name);

        services.Configure<SyncOptions>(name, configuration);
        services.AddOptions<SyncOptions>(name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (name == NamedClients.Default)
        {
            services.Configure<SyncOptions>(configuration);
            services.AddOptions<SyncOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

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
                sp.GetRequiredKeyedService<ISyncApi>(NamedClients.Default));

            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<ISyncClient>(NamedClients.Default));
        }

        return services;
    }

    private static ISyncClient CreateSyncClient(IServiceProvider serviceProvider, object? key)
    {
        var clientName = (string)key!;
        var syncApi = serviceProvider.GetRequiredKeyedService<ISyncApi>(clientName);
        var optionsAccessor = new MonitorBackedSyncOptionsAccessor(
            serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>(),
            clientName);

        return new SyncClient(syncApi, optionsAccessor);
    }

    private static string GetHttpClientName(string name) => $"{HttpClientNamePrefix}{name}";

    private static void EnsureClientNameNotAlreadyRegistered(IServiceCollection services, string name)
    {
        if (!services.Any(d => d.ServiceType == typeof(ISyncClient) && Equals(d.ServiceKey, name)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"A SyncClient with the name '{name}' has already been registered. " +
            $"HTTP client name: '{GetHttpClientName(name)}'. Each client must have a unique name.");
    }

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
            .AddHttpClient(httpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
                var options = optionsMonitor.Get(name);
                httpClient.BaseAddress = new Uri(options.GetBaseUrl(), UriKind.Absolute);

                // Timing is the pipeline's job only when the pipeline is the SDK's own: that one bounds every
                // attempt (see ConfigureDefaultResilience), while HttpClient's 100-second ceiling covers the
                // whole SendAsync - retries and backoff included - and so would clip a pipeline legitimately
                // allowed to run longer. Nothing else bounds a request, so with resilience disabled, or with a
                // caller-supplied pipeline whose shape we cannot know, that ceiling stays and a black-holed
                // connection fails rather than hanging the caller forever. A caller who needs longer than the
                // ceiling raises it through configureHttpClient, which is applied after this.
                if (options.EnableResilience && configureResilience is null)
                {
                    httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
                }
            });

        ConfigureResilienceHandler(httpClientBuilder, $"sync_{name}", name, configureResilience);
        AddMessageHandlers(httpClientBuilder, name);
        ConfigureConnectionRecycling(httpClientBuilder);
        // Applied last, so a consumer can still replace anything set above.
        configureHttpClient?.Invoke(httpClientBuilder);

        services.AddKeyedTransient(name, (sp, _) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(httpClientName);
            return RestService.For<ISyncApi>(httpClient, refitSettings);
        });
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


    /// <summary>
    /// Configures the resilience handler for an HTTP client.
    /// </summary>
    private static void ConfigureResilienceHandler(
        IHttpClientBuilder httpClientBuilder,
        string resilienceHandlerName,
        string clientName,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        httpClientBuilder.AddResilienceHandler(resilienceHandlerName, (builder, context) =>
        {
            var optionsMonitor = context.ServiceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
            var options = optionsMonitor.Get(clientName);

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

    /// <summary>
    /// Adds tracking and authentication message handlers to an HTTP client.
    /// </summary>
    private static void AddMessageHandlers(IHttpClientBuilder httpClientBuilder, string clientName)
    {
        httpClientBuilder.AddHttpMessageHandler(sp => new TrackingHandler(
            sp.GetService<ILogger<TrackingHandler>>()));

        httpClientBuilder.AddHttpMessageHandler(sp => new SyncAuthenticationHandler(
            new MonitorBackedSyncOptionsAccessor(sp.GetRequiredService<IOptionsMonitor<SyncOptions>>(), clientName),
            sp.GetService<ILogger<SyncAuthenticationHandler>>()));
    }

    internal static void ConfigureDefaultResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
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
