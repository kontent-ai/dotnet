
namespace Kontent.Ai.Management.Models.Spaces.Patch;

/// <summary>
/// Represents properties of the space.
/// </summary>
public enum SpacePropertyName
{
    /// <summary>
    /// The space's codename.
    /// </summary>
    [JsonStringEnumMemberName("codename")]
    Codename,

    /// <summary>
    /// The space's name.
    /// </summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>
    /// The root item of the space.
    /// </summary>
    [JsonStringEnumMemberName("root_item")]
    RootItem,

    /// <summary>
    /// The collections belonging to the space.
    /// </summary>
    [JsonStringEnumMemberName("collections")]
    Collections,
}