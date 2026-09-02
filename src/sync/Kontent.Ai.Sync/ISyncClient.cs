namespace Kontent.Ai.Sync;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
/// <remarks>
/// This contract carries no disposal. A client resolved from a container is owned by the container,
/// which releases it; a client from <c>SyncClient.Create</c> owns the private container it was built
/// over and is returned as the concrete <c>SyncClient</c>, which is disposable.
/// </remarks>
public interface ISyncClient
{
    /// <summary>
    /// Initializes content synchronization, establishing the point future deltas are measured from.
    /// </summary>
    /// <remarks>
    /// Produces no content - the useful output is <see cref="ISyncResult.SyncToken"/>. Store it and pass
    /// it to <see cref="GetDeltaAsync"/> or <see cref="EnumerateDeltaAsync"/>.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>A sync result carrying the token to synchronize from.</returns>
    Task<ISyncResult> InitializeSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves delta updates since the last synchronization.
    /// </summary>
    /// <param name="syncToken">The X-Continuation token from a previous sync operation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>A sync result containing delta updates for items, types, languages, and taxonomies.</returns>
    Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the sync feed from <paramref name="syncToken"/>, yielding one result per request until
    /// the API reports no further changes.
    /// </summary>
    /// <remarks>
    /// The feed ends on the first empty response, which is how the Sync API signals that a client has
    /// caught up. That response is not yielded, so an up-to-date client sees an empty sequence.
    /// <para>
    /// Carry the <see cref="ISyncResult.SyncToken"/> of the last yielded result into the next
    /// synchronization. When nothing was yielded there is no newer token, and the one passed in is
    /// still current.
    /// </para>
    /// <para>
    /// A failed request is yielded as a failed result and ends the enumeration, so a caller that never
    /// inspects <see cref="ISyncResult.IsSuccess"/> sees a short feed rather than an exception.
    /// Cancellation throws, as everywhere else in this SDK.
    /// </para>
    /// <para>
    /// Enumeration is lazy - no request is made until the sequence is iterated - so bounding the walk is
    /// the caller's choice, with <c>Take</c> or by breaking out of the loop.
    /// </para>
    /// </remarks>
    /// <param name="syncToken">The X-Continuation token from a previous sync operation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>A lazy sequence of delta responses, one per request.</returns>
    IAsyncEnumerable<ISyncResult<ISyncDeltaResponse>> EnumerateDeltaAsync(string syncToken, CancellationToken cancellationToken = default);
}
