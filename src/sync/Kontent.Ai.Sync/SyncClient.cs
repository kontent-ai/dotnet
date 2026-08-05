using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Extensions;
using Microsoft.Extensions.Logging;
using Polly;

namespace Kontent.Ai.Sync;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
/// <remarks>
/// Two ways in. A container supplies a Refit client it owns, and disposing this type releases nothing.
/// <see cref="SyncClientBuilder"/> uses the other constructor, where the client builds its own
/// <see cref="HttpClient"/> and owns it - which is what makes disposal meaningful on that path.
/// </remarks>
public sealed class SyncClient : ISyncClient, IDisposable, IAsyncDisposable
{
    private readonly ISyncApi _syncApi;
    private readonly ISyncOptionsAccessor _optionsAccessor;
    private readonly HttpClient? _ownedHttpClient;
    private int _disposeState;

    /// <summary>
    /// Creates a client over an already-configured Refit client. The caller owns its lifetime.
    /// </summary>
    internal SyncClient(ISyncApi syncApi, ISyncOptionsAccessor optionsAccessor)
    {
        _syncApi = syncApi ?? throw new ArgumentNullException(nameof(syncApi));
        _optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
    }

    /// <summary>
    /// Creates a client that builds and owns its own HTTP resources, for use without a container.
    /// </summary>
    /// <exception cref="ValidationException">The options fail validation.</exception>
    /// <param name="options">The options to build from.</param>
    /// <param name="configureResilience">Replaces the default resilience pipeline when supplied.</param>
    /// <param name="loggerFactory">Supplies loggers to the handler chain.</param>
    /// <param name="primaryHandler">
    /// Overrides the innermost handler. The test infrastructure injects a mock here to exercise the real
    /// handler chain without a network.
    /// </param>
    internal SyncClient(
        SyncOptions options,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null,
        ILoggerFactory? loggerFactory = null,
        HttpMessageHandler? primaryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);

        _optionsAccessor = new SnapshotSyncOptionsAccessor(options);
        _ownedHttpClient = SyncApiFactory.CreateHttpClient(
            options,
            _optionsAccessor,
            BuildResiliencePipeline(options, configureResilience),
            loggerFactory,
            primaryHandler);

        _syncApi = RestService.For<ISyncApi>(_ownedHttpClient, RefitSettingsProvider.CreateDefaultSettings());
    }

    private string EnvironmentId => _optionsAccessor.Current.EnvironmentId;

    private static ResiliencePipeline<HttpResponseMessage>? BuildResiliencePipeline(
        SyncOptions options,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience)
    {
        if (!options.EnableResilience)
        {
            return null;
        }

        // Mirrors the DI path: a supplied hook fully replaces the default pipeline.
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        if (configureResilience is not null)
        {
            configureResilience(builder);
        }
        else
        {
            ServiceCollectionExtensions.ConfigureDefaultResilience(builder);
        }

        return builder.Build();
    }

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncInitResponse>> InitializeSyncAsync(CancellationToken cancellationToken = default)
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

        _ownedHttpClient?.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
