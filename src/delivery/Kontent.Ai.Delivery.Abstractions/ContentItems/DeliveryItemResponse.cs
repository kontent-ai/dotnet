using System.Text.Json;

namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Represents a response from Kontent.ai Delivery API that contains a single content item
/// with runtime type resolution support.
/// </summary>
/// <remarks>
/// Returned by dynamic single-item queries, where the item may be resolved to a different concrete type
/// at runtime based on the registered <see cref="ITypeProvider"/>.
/// </remarks>
public sealed record DeliveryItemResponse
{
    /// <summary>
    /// Gets the content item. It is runtime-typed when a model is registered for its content type,
    /// otherwise <see cref="IContentItem{IDynamicElements}"/>.
    /// </summary>
    public required IContentItem Item { get; init; }

    /// <summary>
    /// Raw modular content (linked items and rich text components) from the API response.
    /// Needed to resolve linked items and embedded components of an item with no generated model,
    /// whose elements carry only codenames.
    /// </summary>
    public required IReadOnlyDictionary<string, JsonElement> ModularContent { get; init; }
}
