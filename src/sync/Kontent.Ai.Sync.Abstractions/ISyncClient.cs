namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
public interface ISyncClient
{
    /// <summary>
    /// Initializes content synchronization. Returns X-Continuation token via <see cref="ISyncResult{T}.SyncToken"/>.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>A sync result containing the initialization response and sync token.</returns>
    Task<ISyncResult<ISyncInitResponse>> InitializeSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves delta updates since the last synchronization.
    /// </summary>
    /// <param name="syncToken">The X-Continuation token from a previous sync operation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>A sync result containing delta updates for items, assets, types, languages, and taxonomies.</returns>
    Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default);
}
