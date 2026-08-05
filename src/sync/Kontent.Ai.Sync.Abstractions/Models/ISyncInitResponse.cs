namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the response from sync initialization.
/// The actual sync token is returned in the X-Continuation header via <see cref="ISyncResult{T}.SyncToken"/>.
/// </summary>
public interface ISyncInitResponse
{
    // Intentionally empty. The endpoint returns the same shape as a delta response, but the API
    // guarantees every collection in it is empty on initialization, so there is nothing to surface.
    // The token arrives in the X-Continuation header.
}
