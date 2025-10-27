using Kontent.Ai.Sync.Models;
using Refit;

namespace Kontent.Ai.Sync.Api;

/// <summary>
/// Refit interface for Kontent.ai Sync API - Delta synchronization endpoint.
/// </summary>
public partial interface ISyncApi
{
    /// <summary>
    /// Retrieves delta updates since the last synchronization.
    /// </summary>
    /// <param name="environmentId">The environment ID.</param>
    /// <param name="syncToken">The X-Continuation token from a previous sync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delta updates for items, assets, types, languages, and taxonomies.</returns>
    [Get("/{environmentId}/sync")]
    internal Task<IApiResponse<SyncDeltaResponse>> GetDeltaAsync(
        string environmentId,
        [Header("X-Continuation")] string? syncToken = null,
        CancellationToken cancellationToken = default);
}
