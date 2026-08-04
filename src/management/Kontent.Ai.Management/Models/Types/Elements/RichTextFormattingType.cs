
namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Specifies which text formatting is allowed in a rich text element. To allow all formatting, leave the array empty.
/// </summary>
public enum RichTextFormattingType
{
    /// <summary>
    /// Bold
    /// </summary>
    [JsonStringEnumMemberName("bold")]
    Bold,

    /// <summary>
    /// Code
    /// </summary>
    [JsonStringEnumMemberName("code")]
    Code,

    /// <summary>
    /// Italic
    /// </summary>
    [JsonStringEnumMemberName("italic")]
    Italic,

    /// <summary>
    /// Link
    /// </summary>
    [JsonStringEnumMemberName("link")]
    Link,

    /// <summary>
    /// Subscript
    /// </summary>
    [JsonStringEnumMemberName("subscript")]
    Subscript,

    /// <summary>
    /// Superscript
    /// </summary>
    [JsonStringEnumMemberName("superscript")]
    Superscript,

    /// <summary>
    /// Unstyled formatting allows only plain text
    /// </summary>
    [JsonStringEnumMemberName("unstyled")]
    Unstyled,
}
