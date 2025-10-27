using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Refit;

namespace Kontent.Ai.Sync.Extensions;

/// <summary>
/// Extension methods for registering Kontent.ai Sync SDK services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string DefaultName = "Default";

    /// <summary>
    /// Registers the Kontent.ai Sync client with the specified options instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="syncOptions">The sync options instance.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <param name="configureRefit">Optional action to configure Refit settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        SyncOptions syncOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        Action<RefitSettings>? configureRefit = null)
    {
        ArgumentNullException.ThrowIfNull(syncOptions);

        return services.AddSyncClient(
            DefaultName,
            options => {
                options.EnvironmentId = syncOptions.EnvironmentId;
                options.EnableResilience = syncOptions.EnableResilience;
                options.ProductionEndpoint = syncOptions.ProductionEndpoint;
                options.PreviewEndpoint = syncOptions.PreviewEndpoint;
                options.PreviewApiKey = syncOptions.PreviewApiKey;
                options.UsePreviewApi = syncOptions.UsePreviewApi;
            },
            configureHttpClient,
            configureResilience,
            configureRefit);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client with the specified options builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="buildSyncOptions">A function to build the sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <param name="configureRefit">Optional action to configure Refit settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        Func<ISyncOptionsBuilder, SyncOptions> buildSyncOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        Action<RefitSettings>? configureRefit = null)
    {
        ArgumentNullException.ThrowIfNull(buildSyncOptions);

        var builder = SyncOptionsBuilder.CreateInstance();
        var options = buildSyncOptions(builder);

        return services.AddSyncClient(options, configureHttpClient, configureResilience, configureRefit);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="configurationSectionName">The configuration section name. Defaults to "SyncOptions".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = "SyncOptions")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddSyncClient(
            DefaultName,
            options => section.Bind(options));
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

        return services.AddSyncClient(DefaultName, configureOptions);
    }

    /// <summary>
    /// Registers a named Kontent.ai Sync client with the specified configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the sync options.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action to configure resilience policies.</param>
    /// <param name="configureRefit">Optional action to configure Refit settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        Action<SyncOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        Action<RefitSettings>? configureRefit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(configureOptions);

        // Register named options
        services.Configure(name, configureOptions);
        services.AddOptions<SyncOptions>(name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Also configure unnamed options for backward compatibility if this is the default name
        if (name == DefaultName)
        {
            services.Configure(configureOptions);
            services.AddOptions<SyncOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        // Register HTTP client and Refit API
        RegisterNamedHttpClient(services, name, configureHttpClient, configureResilience, configureRefit);

        // Register keyed ISyncClient
        services.AddKeyedSingleton<ISyncClient>(name, (sp, key) =>
        {
            var clientName = (string)key!;
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<SyncOptions>>();
            var syncApi = sp.GetRequiredKeyedService<ISyncApi>(clientName);

            // Create a named options monitor
            var namedMonitor = new NamedOptionsMonitor<SyncOptions>(optionsMonitor, clientName);

            return new SyncClient(syncApi, namedMonitor);
        });

        // Register default client accessors if this is the default name
        if (name == DefaultName)
        {
            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<ISyncApi>(DefaultName));

            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<ISyncClient>(DefaultName));
        }

        return services;
    }

    /// <summary>
    /// Registers and configures a named HTTP client with Refit.
    /// </summary>
    private static void RegisterNamedHttpClient(
        IServiceCollection services,
        string name,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience,
        Action<RefitSettings>? configureRefit)
    {
        var refitSettings = CreateRefitSettings(configureRefit);

        // Register named HTTP client with unique name
        var httpClientName = $"Kontent.Ai.Sync.HttpClient.{name}";
        var httpClientBuilder = services
            .AddHttpClient(httpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
                var options = optionsMonitor.Get(name);
                var baseUrl = options.UsePreviewApi ? options.PreviewEndpoint : options.ProductionEndpoint;
                httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            });

        // Add resilience handler
        ConfigureResilienceHandler(httpClientBuilder, $"sync_{name}", name, configureResilience);

        // Add authentication handler
        AddMessageHandlers(httpClientBuilder, name);

        // Bind Refit client to the HTTP pipeline using typed client pattern
        httpClientBuilder.AddTypedClient(http => RestService.For<ISyncApi>(http, refitSettings));

        // Apply custom configuration
        configureHttpClient?.Invoke(httpClientBuilder);

        // Register keyed ISyncApi - retrieve from HTTP client factory
        services.AddKeyedTransient(name, (sp, _) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(httpClientName);
            return RestService.For<ISyncApi>(httpClient, refitSettings);
        });
    }

    /// <summary>
    /// Creates and configures Refit settings with optional customization.
    /// </summary>
    private static RefitSettings CreateRefitSettings(Action<RefitSettings>? configureRefit)
    {
        var refitSettings = RefitSettingsProvider.CreateDefaultSettings();
        configureRefit?.Invoke(refitSettings);
        return refitSettings;
    }

    /// <summary>
    /// Configures the resilience handler for an HTTP client.
    /// </summary>
    private static void ConfigureResilienceHandler(
        IHttpClientBuilder httpClientBuilder,
        string resilienceHandlerName,
        string optionsName,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        httpClientBuilder.AddResilienceHandler(resilienceHandlerName, (builder, context) =>
        {
            var optionsMonitor = context.ServiceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
            var options = optionsMonitor.Get(optionsName);

            if (!options.EnableResilience)
                return;

            if (configureResilience != null)
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
    /// Adds authentication message handlers to an HTTP client.
    /// </summary>
    private static void AddMessageHandlers(IHttpClientBuilder httpClientBuilder, string optionsName)
    {
        httpClientBuilder.AddHttpMessageHandler(sp => new SyncAuthenticationHandler(
            sp.GetRequiredService<IOptionsMonitor<SyncOptions>>(),
            optionsName));
    }

    private static void ConfigureDefaultResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        // Retry policy
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.IsSuccessStatusCode == false &&
                IsRetryableStatusCode(args.Outcome.Result?.StatusCode))
        });

        // Timeout policy
        builder.AddTimeout(TimeSpan.FromSeconds(30));
    }

    private static bool IsRetryableStatusCode(System.Net.HttpStatusCode? statusCode)
        => statusCode is
            System.Net.HttpStatusCode.TooManyRequests or
            System.Net.HttpStatusCode.RequestTimeout or
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout;
}

/// <summary>
/// Wraps an options monitor to always return options for a specific name.
/// </summary>
internal sealed class NamedOptionsMonitor<T>(IOptionsMonitor<T> inner, string name) : IOptionsMonitor<T>
{
    public T CurrentValue => inner.Get(name);
    public T Get(string? name) => inner.Get(name ?? throw new ArgumentNullException(nameof(name)));
    public IDisposable? OnChange(Action<T, string?> listener) => inner.OnChange(listener);
}
