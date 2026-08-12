namespace Kontent.Ai.Management.Models.Webhooks.Triggers.ContentItem;

/// <summary>
/// Represents content item actions.
/// </summary>
public enum ContentItemAction
{
    /// <summary>
    /// Content item created action.
    /// </summary>
    [JsonStringEnumMemberName("created")]
    Created,

    /// <summary>
    /// Content item changed action.
    /// </summary>
    [JsonStringEnumMemberName("changed")]
    Changed,

    /// <summary>
    /// Content item deleted action.
    /// </summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted,

    /// <summary>
    /// Content item published action.
    /// </summary>
    [JsonStringEnumMemberName("published")]
    Published,

    /// <summary>
    /// Content item unpublished action.
    /// </summary>
    [JsonStringEnumMemberName("unpublished")]
    Unpublished,

    /// <summary>
    /// Content item workflow step changed action.
    /// </summary>
    [JsonStringEnumMemberName("workflow_step_changed")]
    WorkflowStepChanged,

    /// <summary>
    /// Content item metadata changed action.
    /// </summary>
    [JsonStringEnumMemberName("metadata_changed")]
    MetadataChanged
}