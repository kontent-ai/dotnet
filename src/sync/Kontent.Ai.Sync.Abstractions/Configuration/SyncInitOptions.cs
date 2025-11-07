namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Options for filtering content during sync initialization.
/// </summary>
public class SyncInitOptions
{
    /// <summary>
    /// Gets or sets the content type codenames to filter by.
    /// Only items of these types will be included in the sync.
    /// </summary>
    public IEnumerable<string>? ContentTypes { get; set; }

    /// <summary>
    /// Gets or sets the collection codenames to filter by.
    /// Only items from these collections will be included in the sync.
    /// </summary>
    public IEnumerable<string>? Collections { get; set; }

    /// <summary>
    /// Gets or sets the language codename to filter by.
    /// Only items in this language will be included in the sync.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to ignore language fallbacks.
    /// When true, only exact language matches are returned (no fallback to default language).
    /// This requires <see cref="Language"/> to be set.
    /// </summary>
    public bool IgnoreLanguageFallbacks { get; set; }
}
