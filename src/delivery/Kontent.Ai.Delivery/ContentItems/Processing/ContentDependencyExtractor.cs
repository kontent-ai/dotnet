using System.Text.Json;

namespace Kontent.Ai.Delivery.ContentItems.Processing;

/// <summary>
/// Derives cache-invalidation keys from element values, keeping that concern out of
/// <see cref="Mapping.ContentItemMapper"/> and <see cref="RichTextParser"/>.
/// </summary>
internal static class ContentDependencyExtractor
{
    /// <summary>
    /// Tracks the inline images, content links and inline content items a rich text element refers to.
    /// A null context means caching is off, so nothing is extracted.
    /// </summary>
    public static void ExtractFromRichTextElement(
        IRichTextElementValue element,
        DependencyTrackingContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Track inline image dependencies
        if (element.Images != null)
        {
            foreach (var imageId in element.Images.Keys)
            {
                context.TrackAsset(imageId);
            }
        }

        // Track content link dependencies
        if (element.Links != null)
        {
            foreach (var link in element.Links.Values)
            {
                context.TrackItem(link.Codename);
            }
        }

        // Track modular content dependencies (inline content items)
        if (element.ModularContent != null)
        {
            foreach (var codename in element.ModularContent)
            {
                context.TrackItem(codename);
            }
        }
    }

    /// <summary>
    /// Tracks the taxonomy group a taxonomy element draws from, not its individual terms.
    /// A null context means caching is off, so nothing is extracted.
    /// </summary>
    public static void ExtractFromTaxonomyElement(
        JsonElement elementValue,
        DependencyTrackingContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Extract taxonomy group codename for dependency tracking
        if (elementValue.TryGetProperty("taxonomy_group", out var taxonomyGroupEl) &&
            taxonomyGroupEl.ValueKind == JsonValueKind.String)
        {
            var taxonomyGroup = taxonomyGroupEl.GetString();
            context.TrackTaxonomy(taxonomyGroup);
        }
    }
}
