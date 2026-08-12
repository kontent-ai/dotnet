namespace Kontent.Ai.Management.Models.Webhooks.Triggers.Asset;

/// <summary>
/// Represents asset actions.
/// </summary>
public enum AssetAction
{
    /// <summary>
    /// Asset created action.
    /// </summary>
    [JsonStringEnumMemberName("created")]
    Created,

    /// <summary>
    /// Asset changed action.
    /// </summary>
    [JsonStringEnumMemberName("changed")]
    Changed,

    /// <summary>
    /// Asset deleted action.
    /// </summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted,

    /// <summary>
    /// Asset metadata changed action.
    /// </summary>
    [JsonStringEnumMemberName("metadata_changed")]
    MetadataChanged
}