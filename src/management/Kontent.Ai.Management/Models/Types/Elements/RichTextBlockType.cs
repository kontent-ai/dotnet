
namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Specifies which blocks are allowed inside your rich text element.
/// You can allow text, tables, images, and components and items. To allow all blocks, leave the array empty.
/// </summary>
public enum RichTextBlockType
{
    /// <summary>
    /// Text block.
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>
    /// Tables block.
    /// </summary>
    [JsonStringEnumMemberName("tables")]
    Tables,

    /// <summary>
    /// Images block.
    /// </summary>
    [JsonStringEnumMemberName("images")]
    Images,

    /// <summary>
    /// Components and items block.
    /// </summary>
    [JsonStringEnumMemberName("components-and-items")]
    ComponentsAndItems
}
