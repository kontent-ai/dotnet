
namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Represents the allowed file types for the asset element in content types.
/// </summary>
public enum FileType
{
    /// <summary>
    /// Any file type.
    /// </summary>
    [JsonStringEnumMemberName("any")]
    Any,

    /// <summary>
    /// Images that support image transformation.
    /// More info: https://kontent.ai/learn/reference/image-transformation
    /// </summary>
    [JsonStringEnumMemberName("adjustable")]
    Adjustable
}
