using System.Collections;
using System.Collections.Frozen;
using Kontent.Ai.Delivery.ContentItems.RichText.Blocks;

namespace Kontent.Ai.Delivery.ContentItems.RichText;

/// <inheritdoc cref="IRichTextContent" />
public sealed class RichTextContent : IRichTextContent
{
    private readonly IReadOnlyList<IRichTextBlock> _blocks;

    internal RichTextContent(IReadOnlyList<IRichTextBlock> blocks) => _blocks = blocks;

    /// <summary>
    /// The default Kontent.ai empty rich text value, equivalent to <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c>.
    /// </summary>
    public static RichTextContent Empty { get; } = new(
    [
        new HtmlNode(
            "p",
            FrozenDictionary<string, string>.Empty,
            [
                new HtmlNode(
                    "br",
                    FrozenDictionary<string, string>.Empty,
                    [])
            ])
    ]);

    /// <inheritdoc />
    public int Count => _blocks.Count;

    /// <inheritdoc />
    public IRichTextBlock this[int index] => _blocks[index];

    /// <inheritdoc />
    public IEnumerator<IRichTextBlock> GetEnumerator() => _blocks.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
