using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents the response from sync initialization.
/// The actual sync token is returned in the X-Continuation header.
/// </summary>
internal sealed record SyncInitResponse : ISyncInitResponse
{
    // This class is intentionally empty as the sync/init endpoint returns
    // an empty response body. The X-Continuation token is extracted from the headers.
}
