namespace Kontent.Ai.Sync.Api;

internal partial interface ISyncApi
{
    // The body is not read. Initialization returns the same shape as a delta, but the API guarantees every
    // collection in it is empty: the token in the X-Continuation header is the whole result.
    [Post("/v2/{environmentId}/sync/init")]
    internal Task<IApiResponse> InitializeSyncAsync(
        string environmentId,
        CancellationToken cancellationToken = default);
}
