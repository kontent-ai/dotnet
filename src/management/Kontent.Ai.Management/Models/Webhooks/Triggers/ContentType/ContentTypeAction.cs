namespace Kontent.Ai.Management.Models.Webhooks.Triggers.ContentType;

/// <summary>
/// Represents content type actions.
/// </summary>
public enum ContentTypeAction
{
    /// <summary>
    /// Content type created action.
    /// </summary>
    [JsonStringEnumMemberName("created")]
    Created,

    /// <summary>
    /// Content type changed action.
    /// </summary>
    [JsonStringEnumMemberName("changed")]
    Changed,

    /// <summary>
    /// Content type deleted action.
    /// </summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted
}