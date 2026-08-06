namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Represents the result of a cache retrieval, bundling the cached value with its dependency keys.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <param name="Value">The cached value.</param>
/// <param name="DependencyKeys">
/// Canonical dependency keys associated with this cached entry.
/// These keys can be used for downstream cache invalidation scenarios
/// such as ASP.NET output-cache tagging.
/// </param>
public sealed record CacheResult<T>(T Value, IReadOnlyList<string> DependencyKeys) where T : class
{
    /// <summary>
    /// Whether the factory produced this value during this call, rather than it being served from cache.
    /// </summary>
    /// <remarks>
    /// The caller cannot infer this from the factory itself. With eager refresh enabled the factory also
    /// runs on a background thread while the stale-but-valid value is returned immediately, so anything
    /// the factory records is written by a different call than the one reading it. Only the cache knows
    /// which value it handed back.
    /// </remarks>
    public bool FromFactory { get; init; }
}
