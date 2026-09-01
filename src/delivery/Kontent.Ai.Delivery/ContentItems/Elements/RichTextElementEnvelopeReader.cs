using System.Text.Json;
using Kontent.Ai.Delivery.ContentItems.ContentLinks;
using Kontent.Ai.Delivery.ContentItems.RichText.Blocks;

namespace Kontent.Ai.Delivery.ContentItems.Elements;

internal static class RichTextElementEnvelopeReader
{
    /// <remarks>
    /// Owned here rather than taken from the caller. The envelope's three shapes are internal records whose
    /// every property carries an explicit <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>,
    /// so they need nothing from the SDK's configurable options - and holding one instance is what keeps the
    /// typed and the dynamic path reading the same envelope the same way, rather than trusting two callers to
    /// pass the same thing. They did not: the typed path passed nothing at all, leaving
    /// <see cref="InlineImage.Url"/> - a required member - to throw on a recased property that the dynamic
    /// path accepted.
    /// </remarks>
    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RichTextElementData Read(JsonElement envelope, string codename)
        => new()
        {
            Type = GetStringProperty(envelope, "type"),
            Name = GetStringProperty(envelope, "name"),
            Codename = codename,
            Value = GetStringProperty(envelope, "value"),
            Images = DeserializeInlineImages(envelope),
            Links = DeserializeContentLinks(envelope),
            ModularContent = DeserializeModularContent(envelope)
        };

    private static string GetStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop)
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    private static Dictionary<Guid, IInlineImage> DeserializeInlineImages(JsonElement root)
    {
        if (!root.TryGetProperty("images", out var imagesEl) || imagesEl.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<Guid, IInlineImage>();
        foreach (var prop in imagesEl.EnumerateObject())
        {
            if (!Guid.TryParse(prop.Name, out var id))
            {
                continue;
            }

            var image = JsonSerializer.Deserialize<InlineImage>(prop.Value, EnvelopeOptions);
            if (image is not null)
            {
                result[id] = image;
            }
        }

        return result;
    }

    private static Dictionary<Guid, IContentLink> DeserializeContentLinks(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var linksEl) || linksEl.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<Guid, IContentLink>();
        foreach (var prop in linksEl.EnumerateObject())
        {
            if (!Guid.TryParse(prop.Name, out var id))
            {
                continue;
            }

            var link = JsonSerializer.Deserialize<ContentLink>(prop.Value, EnvelopeOptions);
            if (link is null)
            {
                continue;
            }

            result[id] = link with { Id = id };
        }

        return result;
    }

    /// <remarks>
    /// Blank codenames are dropped. The list feeds cache-dependency tracking, which discards them anyway, and
    /// a blank names no content item that could ever be looked up.
    /// </remarks>
    private static List<string> DeserializeModularContent(JsonElement root)
    {
        if (!root.TryGetProperty("modular_content", out var modularEl) || modularEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> codenames = [];
        foreach (var item in modularEl.EnumerateArray())
        {
            if (item.GetString() is { Length: > 0 } codename)
            {
                codenames.Add(codename);
            }
        }

        return codenames;
    }
}
