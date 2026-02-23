using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Extensions;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

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
            SyncClientNames.Default,
            options => SyncOptionsCopyHelper.Copy(syncOptions, options),
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

        return services.AddSyncClient(
            SyncClientNames.Default,
            opts => SyncOptionsCopyHelper.Copy(options, opts),
            configureHttpClient,
            configureResilience,
            configureRefit);
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
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddSyncClientFromConfiguration(SyncClientNames.Default, section);
    }

    /// <summary>
    /// Registers a named Kontent.ai Sync client using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="configurationSectionName">The configuration section name. Defaults to "SyncOptions".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string configurationSectionName = "SyncOptions")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddSyncClientFromConfiguration(name, section);
    }

    /// <summary>
    /// Registers the Kontent.ai Sync client using a configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSection">The configuration section containing sync options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddSyncClientFromConfiguration(SyncClientNames.Default, configurationSection);
    }

    /// <summary>
    /// Registers a named Kontent.ai Sync client using a configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configurationSection">The configuration section containing sync options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddSyncClientFromConfiguration(name, configurationSection);
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

        return services.AddSyncClient(SyncClientNames.Default, configureOptions);
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
            SyncClientNames.Default,
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
    /// <param name="configureRefit">Optional action to configure Refit settings.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the same name is already registered.</exception>
    public static IServiceCollection AddSyncClient(
        this IServiceCollection services,
        string name,
        Action<SyncOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        Action<RefitSettings>? configureRefit = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateClientName(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        EnsureClientNameNotAlreadyRegistered(services, name);

        // Register named options
        services.Configure(name, configureOptions);
        services.AddOptions<SyncOptions>(name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Also configure unnamed options for backward compatibility if this is the default name
        if (name == SyncClientNames.Default)
        {
            services.Configure(configureOptions);
            services.AddOptions<SyncOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience, configureRefit);
    }

    private static IServiceCollection AddSyncClientFromConfiguration(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        Action<RefitSettings>? configureRefit = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateClientName(name);
        ArgumentNullException.ThrowIfNull(configuration);

        EnsureClientNameNotAlreadyRegistered(services, name);

        services.Configure<SyncOptions>(name, configuration);
        services.AddOptions<SyncOptions>(name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (name == SyncClientNames.Default)
        {
            services.Configure<SyncOptions>(configuration);
            services.AddOptions<SyncOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience, configureRefit);
    }

    private static IServiceCollection CompleteClientRegistration(
        IServiceCollection services,
        string name,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        Action<RefitSettings>? configureRefit = null)
    {
        RegisterNamedHttpClient(services, name, configureHttpClient, configureResilience, configureRefit);

        services.AddKeyedSingleton<ISyncClient>(name, CreateSyncClient);
        services.TryAddSingleton<ISyncClientFactory, SyncClientFactory>();

        if (name == SyncClientNames.Default)
        {
            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<ISyncApi>(SyncClientNames.Default));

            services.TryAddSingleton(sp =>
                sp.GetRequiredKeyedService<ISyncClient>(SyncClientNames.Default));
        }

        return services;
    }

    private static ISyncClient CreateSyncClient(IServiceProvider serviceProvider, object? key)
    {
        var clientName = (string)key!;
        var syncApi = serviceProvider.GetRequiredKeyedService<ISyncApi>(clientName);
        var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
        var options = optionsMonitor.Get(clientName);
        return new SyncClient(syncApi, options.EnvironmentId);
    }

    private static string GetHttpClientName(string name) => $"{HttpClientNamePrefix}{name}";

    private static void ValidateClientName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Trim() != name || name.Contains(' '))
        {
            throw new ArgumentException(
                "Client name cannot contain leading/trailing whitespace, or contain spaces. Use underscores or hyphens instead.",
                nameof(name));
        }
    }

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
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience,
        Action<RefitSettings>? configureRefit)
    {
        var refitSettings = CreateRefitSettings(configureRefit);

        var httpClientName = GetHttpClientName(name);
        var httpClientBuilder = services
            .AddHttpClient(httpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
                var options = optionsMonitor.Get(name);
                httpClient.BaseAddress = new Uri(options.GetBaseUrl(), UriKind.Absolute);
            });

        ConfigureResilienceHandler(httpClientBuilder, $"sync_{name}", name, configureResilience);
        AddMessageHandlers(httpClientBuilder, name);
        configureHttpClient?.Invoke(httpClientBuilder);

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
            sp.GetRequiredService<IOptionsMonitor<SyncOptions>>(),
            clientName,
            sp.GetService<ILogger<SyncAuthenticationHandler>>()));
    }

    private static void ConfigureDefaultResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                IsTransientException(args.Outcome.Exception, args.Context.CancellationToken) ||
                (args.Outcome.Result?.IsSuccessStatusCode == false &&
                 IsRetryableStatusCode(args.Outcome.Result?.StatusCode))),
            DelayGenerator = GetRetryAfterDelay
        });

        builder.AddTimeout(TimeSpan.FromSeconds(30));
    }

    private static ValueTask<TimeSpan?> GetRetryAfterDelay(RetryDelayGeneratorArguments<HttpResponseMessage> args)
    {
        if (args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.TooManyRequests } response
            && response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            return ValueTask.FromResult<TimeSpan?>(retryAfter);
        }

        return ValueTask.FromResult<TimeSpan?>(null);
    }

    private static bool IsRetryableStatusCode(System.Net.HttpStatusCode? statusCode)
        => statusCode is
            System.Net.HttpStatusCode.TooManyRequests or
            System.Net.HttpStatusCode.RequestTimeout or
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout;

    private static bool IsTransientException(Exception? exception, CancellationToken requestCancellationToken)
    {
        if (exception is null)
        {
            return false;
        }

        if (exception is OperationCanceledException)
        {
            if (requestCancellationToken.IsCancellationRequested)
            {
                return false;
            }

            return exception is TaskCanceledException || exception.InnerException is TimeoutException;
        }

        return exception is HttpRequestException or TimeoutException;
    }
}
