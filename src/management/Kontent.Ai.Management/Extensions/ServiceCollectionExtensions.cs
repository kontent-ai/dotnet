using Kontent.Ai.Common;
using Kontent.Ai.Common.Clients;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Management.Extensions;

/// <summary>
/// Registers Kontent.ai Management clients.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    private const string ManagementHttpClientPrefix = "Kontent.Ai.Management.HttpClient.";
    private const string SubscriptionHttpClientPrefix = "Kontent.Ai.Management.SubscriptionHttpClient.";

    /// <summary>
    /// Registers the default Management client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client: its options, HTTP clients and resilience.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddManagementClient(this IServiceCollection services, Action<IManagementClientBuilder> configure)
        => services.AddManagementClient(NamedClients.Default, configure);

    /// <summary>
    /// Registers the default Management client from a pre-built options instance.
    /// </summary>
    /// <remarks>
    /// The instance's values are copied onto the options the container materializes; the object itself is
    /// not registered.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The options to copy.</param>
    /// <param name="configure">Configures the client further, after the options are copied.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddManagementClient(this IServiceCollection services, ManagementOptions options, Action<IManagementClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return services.AddManagementClient(NamedClients.Default, management =>
        {
            management.Options.Configure(options.CopyTo);
            configure?.Invoke(management);
        });
    }

    /// <summary>
    /// Registers a named Management client, resolvable through <see cref="IManagementClientFactory"/> or as a
    /// keyed service under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// The API key is read per request through <see cref="IOptionsMonitor{TOptions}"/>, so a rotated key
    /// takes effect without rebuilding the client. The base addresses and the resilience pipeline are read
    /// once, when each HTTP client is first created.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name. Must be unique across all registrations.</param>
    /// <param name="configure">Configures the client: its options, HTTP clients and resilience.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">A client with the same name is already registered.</exception>
    public static IServiceCollection AddManagementClient(this IServiceCollection services, string name, Action<IManagementClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = ClientRegistration.AddClient<ManagementOptions, IManagementClient, ManagementClientBuilder>(
            services,
            name,
            "management client",
            httpClientName: null,
            static (name, services, options) => new ManagementClientBuilder(name, services, options));

        var refitSettings = RefitSettingsProvider.CreateDefaultSettings();
        builder.HttpClient = ClientRegistration.AddTransport<ManagementOptions, IManagementApi>(
            builder,
            Transport<IManagementApi>(EnvironmentHttpClientName(name), name, o => o.EnvironmentScopePath()),
            refitSettings);
        builder.SubscriptionHttpClient = ClientRegistration.AddTransport<ManagementOptions, ISubscriptionApi>(
            builder,
            Transport<ISubscriptionApi>(SubscriptionHttpClientName(name), name, o => o.SubscriptionScopePath()),
            refitSettings);

        ClientRegistration.AddClientServices<IManagementClient, IManagementClientFactory, ManagementClientFactory>(services, name, CreateManagementClient);

        if (name == NamedClients.Default)
        {
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IManagementApi>(NamedClients.Default));
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<ISubscriptionApi>(NamedClients.Default));
        }

        configure(builder);
        return services;
    }

    private static IManagementClient CreateManagementClient(IServiceProvider serviceProvider, object? key)
    {
        var name = (string)key!;
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<ManagementOptions>>().Get(name);
        var managementApi = options.HasEnvironmentId()
            ? serviceProvider.GetRequiredKeyedService<IManagementApi>(name)
            : null;
        var subscriptionApi = options.HasSubscriptionId()
            ? serviceProvider.GetRequiredKeyedService<ISubscriptionApi>(name)
            : null;
        return new ManagementClient(managementApi, subscriptionApi);
    }

    private static string EnvironmentHttpClientName(string name) => $"{ManagementHttpClientPrefix}{name}";

    private static string SubscriptionHttpClientName(string name) => $"{SubscriptionHttpClientPrefix}{name}";
}
