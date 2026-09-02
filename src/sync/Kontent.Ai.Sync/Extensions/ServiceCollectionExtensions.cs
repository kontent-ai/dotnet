using Kontent.Ai.Common;
using Kontent.Ai.Common.Clients;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync;

/// <summary>
/// Registers Kontent.ai Sync clients.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string HttpClientNamePrefix = "Kontent.Ai.Sync.HttpClient.";

    /// <summary>
    /// Registers the default Sync client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client: its options, HTTP client and resilience.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, Action<ISyncClientBuilder> configure)
        => services.AddSyncClient(NamedClients.Default, configure);

    /// <summary>
    /// Registers the default Sync client from a pre-built options instance.
    /// </summary>
    /// <remarks>
    /// The instance's values are copied onto the options the container materializes; the object itself is
    /// not registered.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The options to copy.</param>
    /// <param name="configure">Configures the client further, after the options are copied.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, SyncOptions options, Action<ISyncClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return services.AddSyncClient(NamedClients.Default, sync =>
        {
            sync.Options.Configure(options.CopyTo);
            configure?.Invoke(sync);
        });
    }

    /// <summary>
    /// Registers a named Sync client, resolvable through <see cref="ISyncClientFactory"/> or as a keyed
    /// service under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// The options are read per request through <see cref="IOptionsMonitor{TOptions}"/>, so a reloaded
    /// API key takes effect without rebuilding the client. The base address and the resilience pipeline
    /// are read once, when the HTTP client is first created.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name. Must be unique across all registrations.</param>
    /// <param name="configure">Configures the client: its options, HTTP client and resilience.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">A client with the same name is already registered.</exception>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, string name, Action<ISyncClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = ClientRegistration.AddClient<SyncOptions, ISyncClient, SyncClientBuilder>(
            services,
            name,
            "sync client",
            GetHttpClientName(name),
            static (name, services, options) => new SyncClientBuilder(name, services, options));

        builder.HttpClient = ClientRegistration.AddTransport<SyncOptions, ISyncApi>(
            builder,
            Transport(name),
            RefitSettingsProvider.CreateDefaultSettings());

        ClientRegistration.AddClientServices<ISyncClient, ISyncClientFactory, SyncClientFactory>(services, name, CreateSyncClient);

        configure(builder);
        return services;
    }

    private static TransportRecipe<SyncOptions> Transport(string name) => new(
        HttpClientName: GetHttpClientName(name),
        ResilienceHandlerName: $"sync_{name}",
        BaseAddress: options => new Uri(options.GetBaseUrl(), UriKind.Absolute),
        ResilienceEnabled: options => options.EnableResilience,
        Ceiling: (options, defaultPipeline, fallback) =>
            HttpClientTimeouts.Resolve(options.Timeout, options.EnableResilience && defaultPipeline, fallback),
        DefaultPipeline: DefaultResilience.ConfigureReadPipeline,
        AddHandlers: httpClient =>
        {
            httpClient.AddHttpMessageHandler(sp => new TrackingHandler(sp.GetService<ILogger<TrackingHandler>>()));
            httpClient.AddHttpMessageHandler(sp => new SyncAuthenticationHandler(
                new MonitorBackedOptionsAccessor<SyncOptions>(sp.GetRequiredService<IOptionsMonitor<SyncOptions>>(), name),
                sp.GetService<ILogger<SyncAuthenticationHandler>>()));
        });

    private static ISyncClient CreateSyncClient(IServiceProvider serviceProvider, object? key)
    {
        var clientName = (string)key!;
        var syncApi = serviceProvider.GetRequiredKeyedService<ISyncApi>(clientName);
        var optionsAccessor = new MonitorBackedOptionsAccessor<SyncOptions>(
            serviceProvider.GetRequiredService<IOptionsMonitor<SyncOptions>>(),
            clientName);

        return new SyncClient(syncApi, optionsAccessor);
    }

    /// <summary>
    /// Builds the client <see cref="SyncClient.Create(Action{ISyncClientBuilder})"/> returns: the same
    /// registration the container path runs, drawn from a provider the caller assembled, with the client
    /// owning what it drew and the provider itself. Takes ownership of <paramref name="provider"/> even
    /// when construction fails.
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

    private static string GetHttpClientName(string name) => $"{HttpClientNamePrefix}{name}";
}
