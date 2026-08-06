namespace Kontent.Ai.Sync;

/// <summary>
/// One page of changes since the supplied continuation token.
/// </summary>
public interface ISyncDeltaResponse
{
    /// <summary>Content items that changed.</summary>
    IReadOnlyList<SyncChange<SyncItemData>> Items { get; }

    /// <summary>Content types that changed.</summary>
    IReadOnlyList<SyncChange<SyncTypeData>> Types { get; }

    /// <summary>Languages that changed.</summary>
    IReadOnlyList<SyncChange<SyncLanguageData>> Languages { get; }

    /// <summary>Taxonomy groups that changed.</summary>
    IReadOnlyList<SyncChange<SyncTaxonomyData>> Taxonomies { get; }
}
