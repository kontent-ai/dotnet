namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Represents blocks types that can be used inside tables in your rich text element.
/// </summary>
public enum RichTextTableBlockType
{
    /// <summary>
    /// Text
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>
    /// Images
    /// </summary>
    [JsonStringEnumMemberName("images")]
    Images,
}
