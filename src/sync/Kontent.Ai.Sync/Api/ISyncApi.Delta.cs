using Kontent.Ai.Sync.Models;

namespace Kontent.Ai.Sync.Api;

internal partial interface ISyncApi
{
    [Get("/v2/{environmentId}/sync")]
    internal Task<IApiResponse<SyncDeltaResponse>> GetDeltaAsync(
        string environmentId,
        [Header("X-Continuation")] string syncToken,
        CancellationToken cancellationToken = default);
}
