namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Represents the result of a cache factory function, bundling the value with its dependency tags.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
/// <param name="Value">The value to cache.</param>
/// <param name="Dependencies">
/// Dependency keys for automatic cache invalidation.
/// Use standardized key formats (see <see cref="IDeliveryCacheManager"/> remarks).
/// </param>
/// <remarks>
/// Return <c>null</c> from the factory when the origin has no value for the key - an item that does not
/// exist, or is no longer published. Throw when the origin could not be reached, so that a cache manager
/// with fail-safe can serve a stale copy for the outage but never for the answer.
/// </remarks>
public sealed record CacheEntry<T>(T Value, IEnumerable<string> Dependencies) where T : class;
