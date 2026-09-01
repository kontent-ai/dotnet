using System.Collections.Concurrent;
using System.Text.Json;

namespace Kontent.Ai.Delivery.ContentItems;

/// <summary>
/// Deserializes content item JSON into <see cref="ContentItem{TModel}"/>.
/// </summary>
/// <param name="options">The JSON serializer options to use.</param>
internal sealed class ContentDeserializer(JsonSerializerOptions options)
{
    // Only the runtime-typed overload needs this; the generic one is resolved by the JIT.
    private static readonly ConcurrentDictionary<Type, Type> _contentItemTypes = new();

    /// <summary>
    /// Deserializes an item whose model type is known at compile time.
    /// </summary>
    public ContentItem<TModel> Deserialize<TModel>(JsonElement json)
        => JsonSerializer.Deserialize<ContentItem<TModel>>(json, options)
            ?? throw new JsonException($"Deserialization returned null for ContentItem<{typeof(TModel).Name}>");

    /// <summary>
    /// Deserializes an item whose model type is only known once its content type codename has been
    /// resolved, which is the case for linked items and for runtime typing of dynamic items.
    /// </summary>
    /// <param name="json">The JSON of the content item.</param>
    /// <param name="modelType">The model type (any POCO or <see cref="IDynamicElements"/>).</param>
    public IContentItem Deserialize(JsonElement json, Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        var contentItemType = _contentItemTypes.GetOrAdd(modelType,
            static t => typeof(ContentItem<>).MakeGenericType(t));

        // Safe by construction: the type was built as ContentItem<>, which implements IContentItem.
        return (IContentItem)(JsonSerializer.Deserialize(json, contentItemType, options)
            ?? throw new JsonException($"Deserialization returned null for {contentItemType.Name}"));
    }
}
