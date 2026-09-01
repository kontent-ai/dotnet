using System.ComponentModel.DataAnnotations;
using Kontent.Ai.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// A builder for creating <see cref="ISyncClient"/> instances without dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Build"/> runs the same registration as <c>services.AddSyncClient(...)</c> inside a private
/// container, and the built client owns both the <see cref="HttpClient"/> it draws from it and the
/// container itself - so it returns the concrete <see cref="SyncClient"/>, which is disposable. Dispose
/// it when you are done.
/// </para>
/// <para>
/// For applications using dependency injection, prefer <c>services.AddSyncClient(...)</c>, which hands
/// lifetime to the container and brings <c>IHttpClientFactory</c>, options reloading and named clients.
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
    private Action<ResiliencePipelineBuilder<HttpResponseMessage>>? _configureResilience;
    private Action<IHttpClientBuilder>? _configureHttpClient;
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
    /// Replaces the default resilience pipeline with a custom one. Has no effect when
    /// <see cref="SyncOptions.EnableResilience"/> is <c>false</c> (matching the DI behaviour).
    /// </summary>
    /// <param name="configureResilience">A delegate that configures the resilience pipeline.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configureResilience"/> is null.</exception>
    public SyncClientBuilder WithResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configureResilience)
    {
        ArgumentNullException.ThrowIfNull(configureResilience);
        _configureResilience = configureResilience;
        return this;
    }

    /// <summary>
    /// Configures the underlying HTTP client, applied after the SDK's own configuration. The test
    /// infrastructure uses it to swap the primary handler for a mock and exercise the real chain.
    /// </summary>
    internal SyncClientBuilder ConfigureHttpClient(Action<IHttpClientBuilder> configureHttpClient)
    {
        ArgumentNullException.ThrowIfNull(configureHttpClient);
        _configureHttpClient = configureHttpClient;
        return this;
    }

    /// <summary>
    /// Builds and returns a configured <see cref="ISyncClient"/> instance.
    /// </summary>
    /// <returns>A fully configured <see cref="ISyncClient"/> that should be disposed when no longer needed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="WithOptions"/> was not called.</exception>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException">
    /// Thrown when the options fail validation - a missing environment ID, or a missing API key for the
    /// Preview or Secure mode.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method validates the configuration and builds all required dependencies.
    /// The returned client owns the HTTP client it draws and the private container behind it — dispose it when done.
    /// </para>
    /// <para>
    /// The builder can be used to create multiple client instances, but each call to <see cref="Build"/>
    /// creates a new independent client with its own HTTP client.
    /// </para>
    /// </remarks>
    public SyncClient Build()
    {
        if (_syncOptions is null)
        {
            throw new InvalidOperationException(
                "SyncOptions must be configured. Call WithOptions() before Build().");
        }

        // Validated here, ahead of the container, so the failure is the documented ValidationException
        // rather than the options pipeline's own exception for the same fault.
        Validator.ValidateObject(_syncOptions, new ValidationContext(_syncOptions), validateAllProperties: true);

        var services = new ServiceCollection();

        if (_loggerFactory is not null)
        {
            // Before AddSyncClient: the HTTP client factory registers logging with TryAdd, so the
            // factory supplied here is the one that wins.
            services.AddSingleton(_loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        }

        services.AddSyncClient(_syncOptions, _configureHttpClient, _configureResilience);

        // ValidateOnBuild checks every registration can be constructed; ValidateOnStart needs a host,
        // which this path does not have.
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        return ServiceCollectionExtensions.CreateOwnedSyncClient(provider, NamedClients.Default);
    }
}
