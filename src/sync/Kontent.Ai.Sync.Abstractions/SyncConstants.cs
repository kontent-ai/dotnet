namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Constants for Kontent.ai Sync API.
/// </summary>
public static class SyncConstants
{
    /// <summary>
    /// Maximum number of items returned per entity type (items, assets, types, languages, taxonomies) in a single sync response.
    /// When any entity collection reaches this limit, additional calls with the continuation token are required to retrieve remaining items.
    /// </summary>
    /// <remarks>
    /// Based on Sync API v2 documentation. This value is used to determine if more changes are available via <see cref="ISyncResult{T}.HasMoreChanges"/>.
    /// </remarks>
    public const int MaxItemsPerEntityType = 100;
}
