namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// The dependency keys the SDK tags cached responses with, for <see cref="IDeliveryCacheManager.InvalidateAsync"/>.
/// The scope constants cover every listing of a kind; the methods compose the key for one entity the way
/// the SDK does, so a webhook handler and the cache agree on the exact string.
/// </summary>
public static class DeliveryCacheDependencies
{
    /// <summary>
    /// Synthetic dependency attached to cached item-list query results (for example, <c>GetItems&lt;T&gt;()</c>).
    /// Invalidating this key clears all cached item-list queries for the current cache namespace.
    /// </summary>
    public const string ItemsListScope = "scope_items_list";

    /// <summary>
    /// Synthetic dependency attached to cached content type listing query results (for example, <c>GetTypes()</c>).
    /// Invalidating this key clears all cached content type listing queries for the current cache namespace.
    /// </summary>
    public const string TypesListScope = "scope_types_list";

    /// <summary>
    /// Synthetic dependency attached to cached taxonomy listing query results (for example, <c>GetTaxonomies()</c>).
    /// Invalidating this key clears all cached taxonomy listing queries for the current cache namespace.
    /// </summary>
    public const string TaxonomiesListScope = "scope_taxonomies_list";

    /// <summary>
    /// The key of a content item: <c>item_{codename}</c>. Every cached response that contains the item -
    /// as the subject, in a listing, or through modular content - carries it.
    /// </summary>
    public static string ForItem(string codename) => $"item_{Normalize(codename)}";

    /// <summary>
    /// The key of a content type: <c>type_{codename}</c>. The cached type definition carries it, and so
    /// does every cached response containing an item of that type.
    /// </summary>
    public static string ForType(string codename) => $"type_{Normalize(codename)}";

    /// <summary>
    /// The key of a taxonomy group: <c>taxonomy_{codename}</c>. The cached group carries it, and so does
    /// every cached response whose items have a taxonomy element drawing from it.
    /// </summary>
    public static string ForTaxonomy(string codename) => $"taxonomy_{Normalize(codename)}";

    /// <summary>
    /// The key of an asset: <c>asset_{id}</c>. Every cached response whose items reference the asset, in
    /// an asset element or as a rich text image, carries it.
    /// </summary>
    public static string ForAsset(Guid id) => $"asset_{id:D}";

    // Codenames are lower-case by construction and the SDK compares keys ordinally, so a key composed
    // from a differently-cased copy of one - a webhook payload, a hand-written constant - is normalized
    // here rather than silently matching nothing.
    private static string Normalize(string codename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codename);
        return codename.Trim().ToLowerInvariant();
    }
}
