using System.ComponentModel.DataAnnotations;
using Kontent.Ai.Common;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Polly;

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
    /// Creates a client against the environment described by <paramref name="managementOptions"/>. The returned
    /// instance owns its <see cref="HttpClient"/>s and the private container they were drawn from — dispose it
    /// when you're done. Throws
    /// <see cref="ValidationException"/> if the options fail validation. For DI scenarios prefer
    /// <c>services.AddManagementClient(...)</c>, which hands lifetime management to the container.
    /// </summary>
    public ManagementClient(ManagementOptions managementOptions)
        : this(managementOptions, configureResilience: null)
    {
    }

    internal ManagementClient(
        ManagementOptions managementOptions,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        var (api, subscriptionApi, ownedResources) = BuildDependencies(managementOptions, configureResilience);

        _managementApi = api;
        _subscriptionApi = subscriptionApi;
        _ownedResources = ownedResources;
        _contentConverter = new Conversion.ContentItemEnvelopeConverter();
        _autoScanContentTypes = true;
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

    // Runs the same registration as AddManagementClient inside a private container and draws the clients
    // from it. Validates first so every standalone construction path surfaces ValidationException, rather
    // than the options pipeline's own exception for the same fault once the container reads them.
    private static (IManagementApi? Api, ISubscriptionApi? SubscriptionApi, IDisposable OwnedResources) BuildDependencies(
        ManagementOptions options,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);

        var services = new ServiceCollection();
        services.AddManagementClient(options, configureHttpClient: null, configureResilience);

        // ValidateOnBuild checks every registration can be constructed; ValidateOnStart needs a host,
        // which this path does not have.
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        return ServiceCollectionExtensions.CreateOwnedApis(provider, NamedClients.Default);
    }
}
