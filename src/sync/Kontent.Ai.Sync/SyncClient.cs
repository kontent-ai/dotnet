using System.Runtime.CompilerServices;
using Kontent.Ai.Common;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Sync;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
/// <remarks>
/// Two ways in, one constructor. A container supplies a Refit client it owns and passes nothing to
/// dispose, so disposing this type releases nothing. <see cref="Create(Action{ISyncClientBuilder})"/> runs
/// the same registration in a private container and hands the client that container to own - which is
/// what makes disposal meaningful on that path.
/// </remarks>
public sealed class SyncClient : ISyncClient, IDisposable, IAsyncDisposable
{
    private readonly ISyncApi _syncApi;
    private readonly IOptionsAccessor<SyncOptions> _optionsAccessor;
    private readonly IDisposable? _ownedResources;
    private int _disposeState;

    /// <summary>
    /// Creates a client over an already-configured Refit client.
    /// </summary>
    /// <param name="syncApi">The Refit client to send through.</param>
    /// <param name="optionsAccessor">Supplies the effective options at request time.</param>
    /// <param name="ownedResources">
    /// What this client is responsible for disposing, or <c>null</c> when something else owns the
    /// transport - a container passes nothing, <c>Create</c> passes the private container it built.
    /// </param>
    internal SyncClient(ISyncApi syncApi, IOptionsAccessor<SyncOptions> optionsAccessor, IDisposable? ownedResources = null)
    {
        _syncApi = syncApi ?? throw new ArgumentNullException(nameof(syncApi));
        _optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
        _ownedResources = ownedResources;
    }

    /// <summary>
    /// Builds a client without a container of your own: the same registration as
    /// <c>services.AddSyncClient(configure)</c>, run inside a private container the client owns. Dispose the
    /// client to release it. Use it as a singleton for the lifetime of your application.
    /// </summary>
    /// <param name="configure">Configures the client: its options, HTTP client and resilience.</param>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The options fail validation.</exception>
    public static SyncClient Create(Action<ISyncClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return CreateOwned(services => services.AddSyncClient(configure));
    }

    /// <summary>
    /// Builds a client without a container of your own, from a pre-built options instance.
    /// </summary>
    /// <param name="options">The options to copy.</param>
    /// <param name="configure">Configures the client further, after the options are copied.</param>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The options fail validation.</exception>
    public static SyncClient Create(SyncOptions options, Action<ISyncClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CreateOwned(services => services.AddSyncClient(options, configure));
    }

    private static SyncClient CreateOwned(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        // ValidateOnBuild checks every registration can be constructed; ValidateOnStart needs a host,
        // which this path does not have. The options themselves are validated when first read.
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        try
        {
            // Built through the same factory the container uses, handed the provider as the resource it
            // owns - so disposing this client tears down the provider and everything registered in it.
            return ServiceCollectionExtensions.CreateSyncClient(provider, NamedClients.Default, ownedResources: provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private string EnvironmentId => _optionsAccessor.Current.EnvironmentId;

    /// <inheritdoc/>
    public async Task<ISyncResult> InitializeSyncAsync(CancellationToken cancellationToken = default)
    {
        var rawResponse = await _syncApi.InitializeSyncAsync(EnvironmentId, cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken);

        var rawResponse = await _syncApi.GetDeltaAsync(EnvironmentId, syncToken, cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ISyncResult<ISyncDeltaResponse>> EnumerateDeltaAsync(
        string syncToken,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken);

        var currentToken = syncToken;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await GetDeltaAsync(currentToken, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                yield return result;
                yield break;
            }

            // An empty response is how the API reports that the client has caught up. It carries no
            // changes, so it is not yielded - which means a caller who was already up to date sees an
            // empty sequence and keeps using the token it passed in.
            if (IsEmpty(result.Value))
            {
                yield break;
            }

            yield return result;

            // A fresh token per response is the contract; without it the same page would be requested
            // forever, so stop rather than loop. The caller keeps a token it can resume from.
            if (result.SyncToken == currentToken)
            {
                yield break;
            }

            currentToken = result.SyncToken;
        }
    }

    private static bool IsEmpty(ISyncDeltaResponse delta)
        => delta.Items.Count == 0
        && delta.Types.Count == 0
        && delta.Languages.Count == 0
        && delta.Taxonomies.Count == 0;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _ownedResources?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
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
}
