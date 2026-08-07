using System.Globalization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Urls.ImageTransformation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.AspNetCore.ImageTransformation;

/// <summary>
/// A tag helper that generates img elements based on assets stored in Kontent.ai.
/// </summary>
/// <param name="imageTransformationOptions">
/// Global image transformation defaults. Kept private: Razor binds every public settable property on a
/// tag helper to an HTML attribute unless told otherwise, and this is not one consumers set per element.
/// </param>
[RestrictChildren("media-condition")]
[HtmlTargetElement("img-asset", Attributes = "asset")]
public sealed class AssetTagHelper(IOptions<ImageTransformationOptions>? imageTransformationOptions = null) : TagHelper
{
    internal const string SizesCollection = "sizes";

    /// <summary>
    /// Represents an asset stored in Kontent.ai. This property is mandatory in order to properly generate an img tag.
    /// </summary>
    [HtmlAttributeName("asset")]
    public IAsset? Asset { get; set; }

    /// <summary>
    /// Allows overriding the alt and title attributes of an image.
    /// </summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// The last parameter of the sizes attribute of an image.
    /// </summary>
    [HtmlAttributeName("default-width")]
    public int DefaultWidth { get; set; } = 300;

    /// <summary>
    /// Widths in which a given image is available. This property is used to generate the resulting srcset. This can also be set globally using <see cref="ImageTransformationOptions"/>.
    /// </summary>
    [HtmlAttributeName("responsive-widths")]
    public int[]? ResponsiveWidths
    {
        get => field ?? imageTransformationOptions?.Value.ResponsiveWidths;
        set;
    }

    /// <summary>
    /// Name of an asset rendition to use. When the rendition exists on the asset, its crop is used for <c>src</c>
    /// and <c>srcset</c>/<c>sizes</c> are skipped; <see cref="Format"/>, <see cref="Quality"/>, <see cref="AutoFormat"/>,
    /// and <see cref="Compression"/> still layer on top. Currently Kontent.ai supports only the <c>default</c> rendition.
    /// </summary>
    [HtmlAttributeName("rendition")]
    public string? Rendition { get; set; }

    /// <summary>
    /// Target image format (e.g. <c>webp</c>).
    /// </summary>
    [HtmlAttributeName("format")]
    public ImageFormat? Format { get; set; }

    /// <summary>
    /// Compression quality for lossy formats (1–100).
    /// </summary>
    [HtmlAttributeName("quality")]
    public int? Quality { get; set; }

    /// <summary>
    /// Fit transformation mode (<c>clip</c> / <c>scale</c> / <c>crop</c>).
    /// </summary>
    [HtmlAttributeName("fit")]
    public ImageFitMode? Fit { get; set; }

    /// <summary>
    /// Enables WebP delivery when the browser advertises support.
    /// </summary>
    [HtmlAttributeName("auto-format")]
    public bool AutoFormat { get; set; }

    /// <summary>
    /// WebP compression mode (<c>lossless</c> / <c>lossy</c>). Only meaningful when the delivered format is WebP.
    /// </summary>
    [HtmlAttributeName("compression")]
    public ImageCompression? Compression { get; set; }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Asset == null)
        {
            await base.ProcessAsync(context, output);
            return;
        }

        output.TagName = "img";
        output.TagMode = TagMode.SelfClosing;

        var image = new TagBuilder("img");
        var rendition = ResolveRendition();

        if (rendition != null)
        {
            // Rendition owns layout (width/height/fit/crop). Only encoding-level transforms layer on top;
            // srcset is skipped because a rendition is a single crop, not a set of widths.
            image.MergeAttribute("src", BuildRenditionUrl(rendition));
        }
        else
        {
            var explicitWidth = ParseNumeric(context.AllAttributes["width"]?.Value);
            var explicitHeight = ParseNumeric(context.AllAttributes["height"]?.Value);
            var responsiveWidths = ResponsiveWidths;

            if (responsiveWidths is { Length: > 0 } && explicitWidth == null && explicitHeight == null)
            {
                var srcSet = string.Join(",", responsiveWidths.Select(w =>
                    $"{BuildTransformedUrl(w, null)} {w}w"));
                image.MergeAttribute("srcset", srcSet);

                var sizes = new List<string>();
                context.Items.Add(SizesCollection, sizes);
                await output.GetChildContentAsync();

                var s = string.Join(", ", sizes.Concat(new[] { $"{DefaultWidth}px" }));
                image.MergeAttribute("sizes", s);

                // Fallback src for clients that don't honor srcset — use the largest declared width.
                image.MergeAttribute("src", BuildTransformedUrl(responsiveWidths.Max(), null));
            }
            else
            {
                image.MergeAttribute("src", BuildTransformedUrl(explicitWidth, explicitHeight));
            }
        }

        var titleToUse = Title ?? Asset.Description ?? string.Empty;
        image.MergeAttribute("alt", titleToUse);
        image.MergeAttribute("title", titleToUse);
        output.MergeAttributes(image);
    }

    private IAssetRendition? ResolveRendition()
    {
        if (Asset == null || string.IsNullOrEmpty(Rendition))
        {
            return null;
        }
        Asset.Renditions.TryGetValue(Rendition, out var rendition);
        return rendition;
    }

    private string BuildTransformedUrl(double? width, double? height)
    {
        var builder = new ImageUrlBuilder(Asset!.Url);
        if (width.HasValue) builder.WithWidth(width.Value);
        if (height.HasValue) builder.WithHeight(height.Value);
        if (Fit.HasValue) builder.WithFitMode(Fit.Value);
        ApplyEncodingTransforms(builder);
        return builder.Url.ToString();
    }

    private string BuildRenditionUrl(IAssetRendition rendition)
    {
        var baseUrl = $"{Asset!.Url}?{rendition.Query}";
        var extras = new List<string>(4);
        if (Format.HasValue) extras.Add($"fm={Format.Value.ToString().ToLowerInvariant()}");
        if (Quality.HasValue) extras.Add($"q={Quality.Value}");
        if (AutoFormat) extras.Add("auto=format");
        if (Compression.HasValue) extras.Add($"lossless={(Compression.Value == ImageCompression.Lossless ? "true" : "false")}");
        return extras.Count > 0 ? $"{baseUrl}&{string.Join("&", extras)}" : baseUrl;
    }

    private void ApplyEncodingTransforms(ImageUrlBuilder builder)
    {
        if (Format.HasValue) builder.WithFormat(Format.Value);
        if (Quality.HasValue) builder.WithQuality(Quality.Value);
        if (AutoFormat) builder.WithAutomaticFormat();
        if (Compression.HasValue) builder.WithCompression(Compression.Value);
    }

    /// <summary>
    /// Reads a <c>width</c>/<c>height</c> attribute as a transformation dimension, or <c>null</c> when it
    /// is not one.
    /// </summary>
    /// <remarks>
    /// HTML allows values the image API has no equivalent for - <c>100%</c>, <c>auto</c>, a CSS calc - and
    /// those must still render: the attribute stays on the element and simply does not drive the
    /// transformation. Parsing is invariant because the value is authored in markup and because
    /// <see cref="ImageUrlBuilder"/> formats it back invariantly; reading it in the server's culture made
    /// the round trip asymmetric, so <c>width="1.5"</c> became <c>w=15</c> wherever <c>.</c> groups digits.
    /// </remarks>
    private static double? ParseNumeric(object? value) =>
        double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
