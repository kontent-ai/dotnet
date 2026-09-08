using System.Text.Json;
using System.Text.RegularExpressions;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.ContentItems.Processing;

/// <summary>
/// Derives a response's cache-invalidation keys from its wire JSON: the items, every entry of
/// <c>modular_content</c>, and the assets, taxonomy groups and content items their elements refer to.
/// </summary>
/// <remarks>
/// Read from the wire rather than collected while mapping, so the keys do not depend on which properties a
/// model declares. A raw-JSON cache entry is shared by every model that reads the same item, and the first
/// model to prime it decided the tags for all of them; an untyped or runtime-typed read mapped nothing and
/// tracked nothing beyond the items themselves. The element's <c>type</c> says what it holds, so no model
/// is needed to know where to look.
/// </remarks>
internal static partial class ResponseDependencyExtractor
{
    public static string[] Extract(
        IEnumerable<IContentItem> items,
        IReadOnlyDictionary<string, JsonElement>? modularContent,
        ILogger? logger = null)
    {
        var context = new DependencyTrackingContext();

        foreach (var item in items)
        {
            context.TrackItem(item.System.Codename);
            context.TrackItemType(item.System.Type);

            if (item is IRawContentItem { RawItemJson: { } raw })
            {
                TrackElements(raw, context, logger);
            }
        }

        if (modularContent is not null)
        {
            foreach (var (codename, linked) in modularContent)
            {
                // A component is invalidated through the item that owns it, so a key of its own is dead
                // weight. Its type still matters: the response does contain an item of that type.
                if (!ContentItemJsonHelper.IsComponent(linked))
                {
                    context.TrackItem(codename);
                }

                context.TrackItemType(ContentItemJsonHelper.ExtractContentType(linked));
                TrackElements(linked, context, logger);
            }
        }

        return [.. context.Dependencies];
    }

    private static void TrackElements(JsonElement item, DependencyTrackingContext context, ILogger? logger)
    {
        if (!item.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var envelope in elements.EnumerateObject().Select(element => element.Value))
        {
            if (envelope.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (envelope.TryGetProperty("type", out var type) ? type.GetString() : null)
            {
                case "asset":
                    TrackAssets(envelope, context, logger);
                    break;
                case "taxonomy":
                    TrackTaxonomyGroup(envelope, context);
                    break;
                case "rich_text":
                    TrackRichText(envelope, context);
                    break;
                case "modular_content":
                    TrackCodenames(envelope, "value", context);
                    break;
            }
        }
    }

    private static void TrackAssets(JsonElement envelope, DependencyTrackingContext context, ILogger? logger)
    {
        if (!envelope.TryGetProperty("value", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object)
            {
                TrackAssetFromUrl(asset, context, logger);
            }
        }
    }

    /// <remarks>
    /// An asset element value carries no id, only the URL, whose path is
    /// <c>/{environmentId}/{assetId}/{filename}</c>.
    /// </remarks>
    private static void TrackAssetFromUrl(JsonElement asset, DependencyTrackingContext context, ILogger? logger)
    {
        var url = asset.TryGetProperty("url", out var urlProperty) ? urlProperty.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Segments.Length >= 3
            && Guid.TryParse(uri.Segments[2].Trim('/'), out var assetId))
        {
            context.TrackAsset(assetId);
        }
        else if (logger is not null)
        {
            LoggerMessages.AssetUrlParsingFailed(logger, url);
        }
    }

    /// <summary>Tracks the taxonomy group the element draws from, not its individual terms.</summary>
    private static void TrackTaxonomyGroup(JsonElement envelope, DependencyTrackingContext context)
    {
        if (envelope.TryGetProperty("taxonomy_group", out var group) && group.ValueKind == JsonValueKind.String)
        {
            context.TrackTaxonomy(group.GetString());
        }
    }

    private static void TrackRichText(JsonElement envelope, DependencyTrackingContext context)
    {
        if (envelope.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
        {
            foreach (var image in images.EnumerateObject())
            {
                if (Guid.TryParse(image.Name, out var imageId))
                {
                    context.TrackAsset(imageId);
                }
            }
        }

        if (envelope.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object)
        {
            foreach (var link in links.EnumerateObject().Select(property => property.Value))
            {
                if (link.ValueKind == JsonValueKind.Object
                    && link.TryGetProperty("codename", out var codename)
                    && codename.ValueKind == JsonValueKind.String)
                {
                    context.TrackItem(codename.GetString());
                }
            }
        }

        TrackCodenames(envelope, "modular_content", context);

        // A link to an asset is an ordinary anchor carrying the asset's id; only inline images are listed
        // under `images`, so the HTML is the one place the reference exists.
        if (envelope.TryGetProperty("value", out var html) && html.ValueKind == JsonValueKind.String)
        {
            foreach (Match match in AssetLink().Matches(html.GetString()!))
            {
                if (Guid.TryParse(match.Groups["id"].ValueSpan, out var assetId))
                {
                    context.TrackAsset(assetId);
                }
            }
        }
    }

    private static void TrackCodenames(JsonElement envelope, string property, DependencyTrackingContext context)
    {
        if (!envelope.TryGetProperty(property, out var codenames) || codenames.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var codename in codenames.EnumerateArray())
        {
            if (codename.ValueKind == JsonValueKind.String)
            {
                context.TrackItem(codename.GetString());
            }
        }
    }

    [GeneratedRegex(@"data-asset-id=""(?<id>[^""]*)", RegexOptions.IgnoreCase)]
    private static partial Regex AssetLink();
}
