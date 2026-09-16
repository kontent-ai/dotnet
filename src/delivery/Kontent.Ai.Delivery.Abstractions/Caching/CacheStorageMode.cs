namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Specifies how the cache manager stores Delivery API responses.
/// </summary>
public enum CacheStorageMode
{
    /// <summary>
    /// Stores fully hydrated C# objects. Suitable for in-memory caches (e.g., <c>IMemoryCache</c>)
    /// where object references are preserved directly.
    /// </summary>
    HydratedObject = 0,

    /// <summary>
    /// Stores item and item-list responses as SDK-internal payload records containing raw JSON and
    /// response metadata. Other query families store their response models. Suitable for hybrid/distributed
    /// caches: item payloads avoid serializing hydrated object graphs and are rehydrated on cache hits.
    /// </summary>
    /// <remarks>
    /// When persisting a value, serialize and deserialize it as the supplied generic type.
    /// The payload format is not a public contract; custom persistent stores must clear or isolate entries
    /// when upgrading the SDK.
    /// </remarks>
    RawJson = 1
}
