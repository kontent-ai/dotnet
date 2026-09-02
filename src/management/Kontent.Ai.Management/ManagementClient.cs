using Kontent.Ai.Common;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Management;

/// <summary>
/// Executes requests against the Kontent.ai Management API. Implements <see cref="IDisposable"/> /
/// <see cref="IAsyncDisposable"/> so non-DI consumers can release the underlying <see cref="HttpClient"/> instances;
/// DI-managed instances pass <c>null</c> for <c>ownedResources</c> and Dispose becomes a no-op.
/// </summary>
public sealed partial class ManagementClient : IManagementClient, IDisposable, IAsyncDisposable
{
    private readonly IManagementApi? _managementApi;
    private readonly ISubscriptionApi? _subscriptionApi;
    private readonly IDisposable? _ownedResources;
    private readonly Conversion.ContentItemEnvelopeConverter _contentConverter;
    // When we built the converter ourselves, auto-scan the consumer's generated-models assembly on first use so
    // rich-text component types resolve. When one was injected (tests / advanced callers), trust its registry as-is.
    private readonly bool _autoScanContentTypes;
    private int _disposed;

    // Each scope's Refit client exists only when its identifier was configured, so reaching for one that was
    // not says which option is missing rather than sending a request to a path with an empty segment.
    private IManagementApi ManagementApi => _managementApi
        ?? throw new InvalidOperationException(ManagementOptionsExtensions.EnvironmentIdMissingMessage);

    private ISubscriptionApi SubscriptionApi => _subscriptionApi
        ?? throw new InvalidOperationException(ManagementOptionsExtensions.SubscriptionIdMissingMessage);

    /// <summary>
    /// Creates a client from a pre-built options instance, without a container of your own: the same
    /// registration as <c>services.AddManagementClient(options)</c>, run inside a private container the
    /// client owns. Dispose it when you're done. Equivalent to <see cref="Create(ManagementOptions, Action{IManagementClientBuilder})"/>.
    /// </summary>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The options fail validation.</exception>
    public ManagementClient(ManagementOptions managementOptions)
        : this(BuildOwned(services => services.AddManagementClient(managementOptions)))
    {
    }

    /// <summary>
    /// Builds a client without a container of your own: the same registration as
    /// <c>services.AddManagementClient(configure)</c>, run inside a private container the client owns. Dispose
    /// the client to release it. Use it as a singleton for the lifetime of your application.
    /// </summary>
    /// <param name="configure">Configures the client: its options, HTTP clients and resilience.</param>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The options fail validation.</exception>
    public static ManagementClient Create(Action<IManagementClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new ManagementClient(BuildOwned(services => services.AddManagementClient(configure)));
    }

    /// <summary>
    /// Builds a client without a container of your own, from a pre-built options instance.
    /// </summary>
    /// <param name="options">The options to copy.</param>
    /// <param name="configure">Configures the client further, after the options are copied.</param>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The options fail validation.</exception>
    public static ManagementClient Create(ManagementOptions options, Action<IManagementClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ManagementClient(BuildOwned(services => services.AddManagementClient(options, configure)));
    }

    private ManagementClient((IManagementApi? Api, ISubscriptionApi? SubscriptionApi, IDisposable OwnedResources) owned)
        : this(owned.Api, owned.SubscriptionApi, owned.OwnedResources)
    {
    }

    internal ManagementClient(
        IManagementApi? managementApi,
        ISubscriptionApi? subscriptionApi,
        IDisposable? ownedResources = null,
        Conversion.ContentItemEnvelopeConverter? contentConverter = null)
    {
        _managementApi = managementApi;
        _subscriptionApi = subscriptionApi;
        _ownedResources = ownedResources;
        _contentConverter = contentConverter ?? new Conversion.ContentItemEnvelopeConverter();
        _autoScanContentTypes = contentConverter is null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ownedResources?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownedResources is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _ownedResources?.Dispose();
        }
    }

    // Runs the registration inside a private container and draws the clients from it. The options are
    // validated when first read, which CreateOwnedApis does, so every standalone path fails the same way.
    private static (IManagementApi? Api, ISubscriptionApi? SubscriptionApi, IDisposable OwnedResources) BuildOwned(
        Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);

        var services = new ServiceCollection();
        register(services);

        // ValidateOnBuild checks every registration can be constructed; ValidateOnStart needs a host,
        // which this path does not have.
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        return ServiceCollectionExtensions.CreateOwnedApis(provider, NamedClients.Default);
    }
}
