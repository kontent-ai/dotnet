namespace Kontent.Ai.Sync.Api;

/// <summary>
/// Refit interface for Kontent.ai Sync API - Initialization endpoint.
/// </summary>
internal partial interface ISyncApi
{
    /// <summary>
    /// Initializes content synchronization.
    /// Returns an X-Continuation token in the response headers.
    /// </summary>
    /// <param name="environmentId">The environment identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response carrying the X-Continuation token in its headers.</returns>
    /// <remarks>
    /// The body is not read. Initialization returns the same shape as a delta, but the API guarantees
    /// every collection in it is empty, so there is nothing to deserialize.
    /// </remarks>
    [Post("/v2/{environmentId}/sync/init")]
    internal Task<IApiResponse> InitializeSyncAsync(
        string environmentId,
        CancellationToken cancellationToken = default);
}
