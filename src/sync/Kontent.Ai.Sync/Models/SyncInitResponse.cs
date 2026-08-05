namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents the response from sync initialization.
/// The sync token is returned in the X-Continuation header.
/// </summary>
internal sealed record SyncInitResponse : ISyncInitResponse
{
    // Intentionally empty. The endpoint returns the same shape as a delta response, but the API
    // guarantees every collection in it is empty on initialization, so there is nothing to surface.
    // The token arrives in the X-Continuation header.
}
