using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Metadata of a content item that changed.
/// </summary>
public sealed record SyncItemData
{
    /// <summary>
    /// The content item's system properties.
    /// </summary>
    [JsonPropertyName("system")]
    public required SyncItemSystem System { get; init; }
}

/// <summary>
/// System properties of a content item in the sync feed.
/// </summary>
/// <remarks>
/// The API also returns a deprecated <c>sitemap_locations</c> array, which is scheduled for removal
/// and is deliberately not surfaced here.
/// </remarks>
public sealed record SyncItemSystem
{
    /// <summary>The content item's internal ID.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>The item's collection codename; <c>default</c> where collections are not enabled.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The content item's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The content item's codename.</summary>
    [JsonPropertyName("codename")]
    public required string Codename { get; init; }

    /// <summary>The codename of the language the content is in.</summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>The codename of the item's content type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// When the item's user content last changed. Moving an item through workflow does not affect it.
    /// </summary>
    [JsonPropertyName("last_modified")]
    public required DateTime LastModified { get; init; }

    /// <summary>The codename of the item's current workflow; absent for components.</summary>
    [JsonPropertyName("workflow")]
    public string? Workflow { get; init; }

    /// <summary>The codename of the item's current workflow step; absent for components.</summary>
    [JsonPropertyName("workflow_step")]
    public string? WorkflowStep { get; init; }
}
