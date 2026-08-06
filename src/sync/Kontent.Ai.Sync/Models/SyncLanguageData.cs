using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Metadata of a language that changed.
/// </summary>
public sealed record SyncLanguageData
{
    /// <summary>
    /// The language's system properties.
    /// </summary>
    [JsonPropertyName("system")]
    public required SyncLanguageSystem System { get; init; }
}

/// <summary>
/// System properties of a language in the sync feed.
/// </summary>
/// <remarks>
/// The narrowest of the four payloads, and the reason they are not one type: a language carries no
/// <c>last_modified</c>, and the API documents none of its three properties as guaranteed.
/// </remarks>
public sealed record SyncLanguageSystem
{
    /// <summary>The language's ID.</summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>The language's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The language's codename.</summary>
    [JsonPropertyName("codename")]
    public string? Codename { get; init; }
}
