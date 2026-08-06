using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Metadata of a taxonomy group that changed.
/// </summary>
public sealed record SyncTaxonomyData
{
    /// <summary>
    /// The taxonomy group's system properties.
    /// </summary>
    [JsonPropertyName("system")]
    public required SyncTaxonomySystem System { get; init; }
}

/// <summary>
/// System properties of a taxonomy group in the sync feed.
/// </summary>
/// <remarks>
/// Identical in shape to <see cref="SyncTypeSystem"/> today. Kept separate because the two describe
/// different entities and the API is free to extend either on its own.
/// </remarks>
public sealed record SyncTaxonomySystem
{
    /// <summary>The taxonomy group's internal ID.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>The taxonomy group's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The taxonomy group's codename.</summary>
    [JsonPropertyName("codename")]
    public required string Codename { get; init; }

    /// <summary>When the taxonomy group last changed.</summary>
    [JsonPropertyName("last_modified")]
    public required DateTimeOffset LastModified { get; init; }
}
