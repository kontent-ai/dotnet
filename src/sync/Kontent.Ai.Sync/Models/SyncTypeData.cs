using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Metadata of a content type that changed.
/// </summary>
public sealed record SyncTypeData
{
    /// <summary>
    /// The content type's system properties.
    /// </summary>
    [JsonPropertyName("system")]
    public required SyncTypeSystem System { get; init; }
}

/// <summary>
/// System properties of a content type in the sync feed.
/// </summary>
public sealed record SyncTypeSystem
{
    /// <summary>The content type's internal ID.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>The content type's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The content type's codename.</summary>
    [JsonPropertyName("codename")]
    public required string Codename { get; init; }

    /// <summary>When the content type last changed.</summary>
    [JsonPropertyName("last_modified")]
    public required DateTime LastModified { get; init; }
}
