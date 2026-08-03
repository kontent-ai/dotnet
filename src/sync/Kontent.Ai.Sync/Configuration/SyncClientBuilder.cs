using Kontent.Ai.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// A builder for creating <see cref="ISyncClient"/> instances without dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// This builder provides a fluent API for configuring and instantiating <see cref="ISyncClient"/>
/// outside of a DI container. Internally, it uses a private <see cref="IServiceCollection"/>
/// to resolve all dependencies, ensuring the same functionality as DI-registered clients.
/// </para>
/// <para>
/// <b>Lifecycle:</b> The returned <see cref="ISyncClient"/> is thread-safe and should be used
/// as a singleton for the lifetime of your application. Each <see cref="Build"/> call creates
/// a new independent client with its own HTTP client. Do not create multiple client instances
/// unless you specifically need isolated configurations.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// // Simple usage with Production API
/// await using var client = SyncClientBuilder
///     .WithOptions(opts => opts
///         .WithEnvironmentId("your-environment-id")
///         .UseProductionApi()
///         .Build())
///     .Build();
///
/// // With Preview API and custom logger factory
/// await using var client2 = SyncClientBuilder
///     .WithOptions(opts => opts
///         .WithEnvironmentId("your-environment-id")
///         .UsePreviewApi("preview-api-key")
///         .Build())
///     .WithLoggerFactory(loggerFactory)
///     .Build();
/// </code>
/// </para>
/// </remarks>
public sealed class SyncClientBuilder
{
    private SyncOptions? _syncOptions;
    private Action<IServiceCollection>? _configureAdditionalServices;
    private ILoggerFactory? _loggerFactory;

    private SyncClientBuilder() { }

    /// <summary>
    /// Creates a builder with configuration via the options builder.
    /// </summary>
    /// <param name="buildSyncOptions">A delegate that creates an instance of the <see cref="SyncOptions"/> using the specified <see cref="ISyncOptionsBuilder"/>.</param>
    /// <returns>A builder for optional client configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buildSyncOptions"/> is null.</exception>
    public static SyncClientBuilder WithOptions(Func<ISyncOptionsBuilder, SyncOptions> buildSyncOptions)
    {
        ArgumentNullException.ThrowIfNull(buildSyncOptions);

        return new SyncClientBuilder
        {
            _syncOptions = buildSyncOptions(SyncOptionsBuilder.CreateInstance())
        };
    }

    /// <summary>
    /// Sets a custom logger factory for diagnostic logging.
    /// </summary>
    /// <param name="loggerFactory">
    /// The logger factory instance. Use your preferred logging framework (Serilog, NLog, etc.)
    /// or Microsoft.Extensions.Logging directly.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loggerFactory"/> is null.</exception>
    /// <remarks>
    /// If not set, logging is disabled (no logging services are registered).
    /// </remarks>
    public SyncClientBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Configures additional services on the internal <see cref="IServiceCollection"/> used to build the client.
    /// </summary>
    /// <param name="configure">A delegate to configure additional services (e.g., custom handlers, replacements).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This is a general-purpose extensibility point. Multiple calls are cumulative — each delegate
    /// is invoked in the order it was registered, AFTER the core sync client services have been registered.
    /// This allows callers to override or replace registrations.
    /// </para>
    /// </remarks>
    public SyncClientBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var previous = _configureAdditionalServices;
        _configureAdditionalServices = previous is null
            ? configure
            : services => { previous(services); configure(services); };
        return this;
    }

    /// <summary>
    /// Builds and returns a configured <see cref="ISyncClient"/> instance.
    /// </summary>
    /// <returns>A fully configured <see cref="ISyncClient"/> that should be disposed when no longer needed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when validation fails (e.g., missing environment ID or API key).
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method validates the configuration and builds all required dependencies.
    /// The returned client owns its dependencies (HTTP client, etc.) — dispose it when done.
    /// </para>
    /// <para>
    /// The builder can be used to create multiple client instances, but each call to <see cref="Build"/>
    /// creates a new independent client with its own HTTP client.
    /// </para>
    /// </remarks>
    public ISyncClient Build()
    {
        if (_syncOptions is null)
        {
            throw new InvalidOperationException(
                "SyncOptions must be configured. Call WithOptions() before Build().");
        }

        var services = new ServiceCollection();

        if (_loggerFactory is not null)
        {
            services.AddSingleton(_loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        }

        services.AddSyncClient(_syncOptions);
        _configureAdditionalServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var client = serviceProvider.GetRequiredService<ISyncClient>();

        return new OwnedSyncClient(serviceProvider, client);
    }
}
