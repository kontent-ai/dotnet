using Kontent.Ai.Sync.Models;
using Refit;

namespace Kontent.Ai.Sync.Api;

/// <summary>
/// Refit interface for Kontent.ai Sync API - Initialization endpoint.
/// </summary>
public partial interface ISyncApi
{
    /// <summary>
    /// Initializes content synchronization.
    /// Returns an X-Continuation token in the response headers.
    /// </summary>
    /// <param name="environmentId">The environment ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Empty response with X-Continuation token in headers.</returns>
    [Post("/{environmentId}/sync/init")]
    internal Task<IApiResponse<SyncInitResponse>> InitializeSyncAsync(
        string environmentId,
        CancellationToken cancellationToken = default);
}
