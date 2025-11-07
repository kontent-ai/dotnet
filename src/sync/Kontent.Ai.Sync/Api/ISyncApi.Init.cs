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
    /// <param name="contentTypes">Optional content type codenames to filter by.</param>
    /// <param name="collections">Optional collection codenames to filter by.</param>
    /// <param name="systemLanguage">Optional language codename to filter by (system.language parameter).</param>
    /// <param name="language">Optional language codename (language parameter, used with systemLanguage to ignore fallbacks).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Empty response with X-Continuation token in headers.</returns>
    [Post("/{environmentId}/sync/init")]
    internal Task<IApiResponse<SyncInitResponse>> InitializeSyncAsync(
        string environmentId,
        [Query(CollectionFormat.Multi)] [AliasAs("system.type")] IEnumerable<string>? contentTypes = null,
        [Query(CollectionFormat.Multi)] [AliasAs("system.collection")] IEnumerable<string>? collections = null,
        [Query] [AliasAs("system.language")] string? systemLanguage = null,
        [Query] string? language = null,
        CancellationToken cancellationToken = default);
}
