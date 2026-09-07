using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.AspNetCore.Html;

namespace Kontent.Ai.AspNetCore.RichText;

/// <summary>
/// Razor-friendly extension methods for rendering Kontent.ai rich-text content.
/// </summary>
public static class RichTextExtensions
{
    /// <summary>
    /// Renders structured rich-text content as an <see cref="IHtmlContent"/> suitable for inclusion in a Razor view:
    /// the Delivery SDK's <see cref="Delivery.RichTextExtensions.ToHtmlAsync"/>, wrapped so Razor does not encode it.
    /// </summary>
    /// <param name="richText">The structured rich-text content.</param>
    /// <param name="resolver">
    /// Optional <see cref="IHtmlResolver"/>. When <c>null</c>, the SDK's built-in defaults are used - not the resolver
    /// registered with <c>AddKontentRichText</c>, which an extension method cannot reach; pass that one explicitly.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="IHtmlContent"/> wrapping the resolver's HTML output.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="richText"/> is <c>null</c>.</exception>
    public static async Task<IHtmlContent> ToHtmlContentAsync(
        this IRichTextContent richText,
        IHtmlResolver? resolver = null,
        CancellationToken cancellationToken = default)
        => new HtmlString(await richText.ToHtmlAsync(resolver, cancellationToken));
}
