namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents delta updates from a sync operation.
/// </summary>
public interface ISyncDeltaResponse
{
    /// <summary>
    /// Gets the list of delta updates for content items that changed since the last synchronization.
    /// If there haven't been any changes, the array is empty. Maximum 100 items per response.
    /// </summary>
    IReadOnlyList<ISyncItem> Items { get; }

    /// <summary>
    /// Gets the list of delta updates for assets that changed since the last synchronization.
    /// If there haven't been any changes, the array is empty. Maximum 100 items per response.
    /// </summary>
    IReadOnlyList<ISyncAsset> Assets { get; }

    /// <summary>
    /// Gets the list of delta updates for content types that changed since the last synchronization.
    /// If there haven't been any changes, the array is empty. Maximum 100 items per response.
    /// </summary>
    IReadOnlyList<ISyncType> Types { get; }

    /// <summary>
    /// Gets the list of delta updates for languages that changed since the last synchronization.
    /// If there haven't been any changes, the array is empty. Maximum 100 items per response.
    /// </summary>
    IReadOnlyList<ISyncLanguage> Languages { get; }

    /// <summary>
    /// Gets the list of delta updates for taxonomy groups that changed since the last synchronization.
    /// If there haven't been any changes, the array is empty. Maximum 100 items per response.
    /// </summary>
    IReadOnlyList<ISyncTaxonomy> Taxonomies { get; }
}
