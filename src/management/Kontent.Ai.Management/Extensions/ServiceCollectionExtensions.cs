using Kontent.Ai.Common;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Management.Extensions;

/// <summary>
/// Registers the Kontent.ai Management client and its dependencies on an <see cref="IServiceCollection"/>.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    private const string ManagementHttpClientPrefix = "Kontent.Ai.Management.HttpClient.";
    private const string SubscriptionHttpClientPrefix = "Kontent.Ai.Management.SubscriptionHttpClient.";

    /// <summary>
    /// Registers the management client using an <see cref="IConfiguration"/> section. Defaults to section
    /// <c>"ManagementOptions"</c> if the name is omitted.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind <see cref="ManagementOptions"/> from.</param>
    /// <param name="configurationSectionName">The section to bind; the whole configuration when empty.</param>
    /// <param name="configureHttpClient">Optional hook on the underlying <see cref="IHttpClientBuilder"/> (both env and subscription clients).</param>
    /// <param name="configureResilience">Optional hook to replace the default resilience pipeline.</param>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = ManagementOptions.DefaultConfigurationSectionName,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddManagementClientFromConfiguration(NamedClients.Default, section, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers a named management client using an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Client name. Must be unique across all registrations.</param>
    /// <param name="configuration">The configuration to bind <see cref="ManagementOptions"/> from.</param>
    /// <param name="configurationSectionName">The section to bind; the whole configuration when empty.</param>
    /// <param name="configureHttpClient">Optional hook on the underlying <see cref="IHttpClientBuilder"/> (both env and subscription clients).</param>
    /// <param name="configureResilience">Optional hook to replace the default resilience pipeline.</param>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string configurationSectionName = ManagementOptions.DefaultConfigurationSectionName,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = string.IsNullOrWhiteSpace(configurationSectionName)
            ? configuration
            : configuration.GetSection(configurationSectionName);

        return services.AddManagementClientFromConfiguration(name, section, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the management client using an <see cref="IConfigurationSection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSection">The configuration section to bind <see cref="ManagementOptions"/> from.</param>
    /// <param name="configureHttpClient">Optional hook on the underlying <see cref="IHttpClientBuilder"/> (both env and subscription clients).</param>
    /// <param name="configureResilience">Optional hook to replace the default resilience pipeline.</param>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddManagementClientFromConfiguration(NamedClients.Default, configurationSection, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers a named management client using an <see cref="IConfigurationSection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Client name. Must be unique across all registrations.</param>
    /// <param name="configurationSection">The configuration section to bind <see cref="ManagementOptions"/> from.</param>
    /// <param name="configureHttpClient">Optional hook on the underlying <see cref="IHttpClientBuilder"/> (both env and subscription clients).</param>
    /// <param name="configureResilience">Optional hook to replace the default resilience pipeline.</param>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        string name,
        IConfigurationSection configurationSection,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddManagementClientFromConfiguration(name, configurationSection, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the default management client with an options-configuration action.
    /// </summary>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        Action<ManagementOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.AddManagementClient(NamedClients.Default, configureOptions);
    }

    /// <summary>
    /// Registers the default management client with options + HTTP/resilience customisation.
    /// </summary>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        Action<ManagementOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        return services.AddManagementClient(
            NamedClients.Default,
            configureOptions,
            configureHttpClient,
            configureResilience);
    }

    /// <summary>
    /// Registers a named management client. This is the primary overload all others delegate to.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Client name. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the management options.</param>
    /// <param name="configureHttpClient">Optional hook on the underlying <see cref="IHttpClientBuilder"/> (both env and subscription clients).</param>
    /// <param name="configureResilience">Optional hook to replace the default resilience pipeline.</param>
    /// <exception cref="InvalidOperationException">A client with the same name is already registered.</exception>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        string name,
        Action<ManagementOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        KeyedClients.EnsureNotRegistered<IManagementClient>(services, name, "management client");

        RegisterOptions(services, name, builder => builder.Configure(configureOptions));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Management client, configuring options with access to the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <remarks>
    /// Use when the options depend on something else in the container - a secret store, a tenant resolver.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the options with access to the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        Action<IServiceProvider, ManagementOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
        => services.AddManagementClient(NamedClients.Default, configureOptions, configureHttpClient, configureResilience);

    /// <summary>
    /// Registers a named Kontent.ai Management client, configuring options with access to the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the client. Must be unique across all registrations.</param>
    /// <param name="configureOptions">Action to configure the options with access to the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the same name is already registered.</exception>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        string name,
        Action<IServiceProvider, ManagementOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        KeyedClients.EnsureNotRegistered<IManagementClient>(services, name, "management client");

        RegisterOptions(services, name, builder =>
            builder.Configure<IServiceProvider>((opts, sp) => configureOptions(sp, opts)));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers the Kontent.ai Management client with the specified options instance.
    /// </summary>
    /// <remarks>
    /// The instance's values are copied onto the options the container materializes; the object itself is
    /// not registered.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="managementOptions">The management options instance.</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <param name="configureResilience">Optional action replacing the default resilience pipeline.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddManagementClient(
        this IServiceCollection services,
        ManagementOptions managementOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(managementOptions);

        return services.AddManagementClient(
            NamedClients.Default,
            options => OptionsCopier<ManagementOptions>.Copy(managementOptions, options),
            configureHttpClient,
            configureResilience);
    }

    private static IServiceCollection AddManagementClientFromConfiguration(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttpClient = null,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        ArgumentNullException.ThrowIfNull(configuration);

        KeyedClients.EnsureNotRegistered<IManagementClient>(services, name, "management client");

        RegisterOptions(services, name, builder => builder.Bind(configuration));

        return CompleteClientRegistration(services, name, configureHttpClient, configureResilience);
    }

    /// <summary>
    /// Registers and validates the options under <paramref name="name"/>, and unnamed as well for the
    /// default client so <c>IOptions&lt;ManagementOptions&gt;</c> resolves without one.
    /// </summary>
    private static void RegisterOptions(
        IServiceCollection services,
        string name,
        Action<OptionsBuilder<ManagementOptions>> configure)
    {
        Register(services.AddOptions<ManagementOptions>(name));

        if (name == NamedClients.Default)
        {
            Register(services.AddOptions<ManagementOptions>());
        }

        void Register(OptionsBuilder<ManagementOptions> builder)
        {
            configure(builder);
            builder.ValidateDataAnnotations().ValidateOnStart();
        }
    }

    private static IServiceCollection CompleteClientRegistration(
        IServiceCollection services,
        string name,
        Action<IHttpClientBuilder>? configureHttpClient,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        var refitSettings = RefitSettingsProvider.CreateDefaultSettings();

        RegisterRefitClient<IManagementApi>(
            services,
            name,
            $"{ManagementHttpClientPrefix}{name}",
            o => o.EnvironmentScopePath(),
            refitSettings,
            configureHttpClient,
            configureResilience);

        RegisterRefitClient<ISubscriptionApi>(
            services,
            name,
            $"{SubscriptionHttpClientPrefix}{name}",
            o => o.SubscriptionScopePath(),
            refitSettings,
            configureHttpClient,
            configureResilience);

        services.AddKeyedSingleton<IManagementClient>(name, CreateManagementClient);
        services.TryAddSingleton<IManagementClientFactory, ManagementClientFactory>();

        if (name == NamedClients.Default)
        {
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IManagementClient>(NamedClients.Default));
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IManagementApi>(NamedClients.Default));
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<ISubscriptionApi>(NamedClients.Default));
        }

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

}
