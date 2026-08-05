using System.Runtime.CompilerServices;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Extensions;

namespace Kontent.Ai.Sync;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
/// <param name="syncApi">The Refit-generated API client.</param>
/// <param name="environmentId">The environment identifier.</param>
internal sealed class SyncClient(
    ISyncApi syncApi,
    string environmentId) : ISyncClient
{
    private readonly ISyncApi _syncApi = syncApi ?? throw new ArgumentNullException(nameof(syncApi));
    private readonly string _environmentId = !string.IsNullOrWhiteSpace(environmentId)
        ? environmentId
        : throw new ArgumentException("Environment ID cannot be null or empty.", nameof(environmentId));

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncInitResponse>> InitializeSyncAsync(CancellationToken cancellationToken = default)
    {
        var rawResponse = await _syncApi.InitializeSyncAsync(_environmentId, cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken);

        var rawResponse = await _syncApi.GetDeltaAsync(_environmentId, syncToken, cancellationToken)
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

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
