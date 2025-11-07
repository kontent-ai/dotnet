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
    /// <returns>A sync result containing delta updates for items, types, languages, and taxonomies.</returns>
    Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all delta updates since the last synchronization by automatically handling pagination.
    /// Continues fetching until no more changes are available or the maximum page limit is reached.
    /// </summary>
    /// <param name="syncToken">The X-Continuation token from a previous sync operation.</param>
    /// <param name="maxPages">Optional maximum number of pages to fetch. Use to limit API calls and control costs.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>A result containing all fetched delta responses and the final sync token.</returns>
    Task<ISyncAllDeltaResult> GetAllDeltaAsync(string syncToken, int? maxPages = null, CancellationToken cancellationToken = default);
}
