namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Represents block types that can be used inside your rich text element.
/// </summary>
public enum RichTextTextBlockType
{
    /// <summary>
    /// Ordered list
    /// </summary>
    [JsonStringEnumMemberName("ordered-list")]
    OrderedList,

    /// <summary>
    /// UnorderedList
    /// </summary>
    [JsonStringEnumMemberName("unordered-list")]
    UnorderedList,

    /// <summary>
    /// Paragraph
    /// </summary>
    [JsonStringEnumMemberName("paragraph")]
    Paragraph,

    /// <summary>
    /// HeadingOne
    /// </summary>
    [JsonStringEnumMemberName("heading-one")]
    HeadingOne,

    /// <summary>
    /// HeadingTwo
    /// </summary>
    [JsonStringEnumMemberName("heading-two")]
    HeadingTwo,

    /// <summary>
    /// HeadingThree
    /// </summary>
    [JsonStringEnumMemberName("heading-three")]
    HeadingThree,

    /// <summary>
    /// HeadingFour
    /// </summary>
    [JsonStringEnumMemberName("heading-four")]
    HeadingFour,

    /// <summary>
    /// HeadingFive
    /// </summary>
    [JsonStringEnumMemberName("heading-five")]
    HeadingFive,

    /// <summary>
    /// HeadingSix
    /// </summary>
    [JsonStringEnumMemberName("heading-six")]
    HeadingSix
}
