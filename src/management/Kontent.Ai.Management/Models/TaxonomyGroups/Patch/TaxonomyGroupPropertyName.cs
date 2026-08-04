
namespace Kontent.Ai.Management.Models.TaxonomyGroups.Patch;

/// <summary>
/// Represents enum of properties that can be replaced in the taxonomy group.
/// </summary>
public enum TaxonomyGroupPropertyName
{
    /// <summary>
    /// The taxonomy group's codename.
    /// </summary>
    [JsonStringEnumMemberName("codename")]
    Codename,

    /// <summary>
    /// The taxonomy group's display name.
    /// </summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>
    /// All terms in the taxonomy group.
    /// </summary>
    [JsonStringEnumMemberName("terms")]
    Terms,
}
