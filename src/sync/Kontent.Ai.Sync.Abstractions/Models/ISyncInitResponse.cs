namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the response from sync initialization.
/// The actual sync token is returned in the X-Continuation header via <see cref="ISyncResult{T}.SyncToken"/>.
/// </summary>
public interface ISyncInitResponse
{
    // This interface is intentionally empty as the sync/init endpoint returns
    // an empty response body. The X-Continuation token is extracted from the headers.
}
