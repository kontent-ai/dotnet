using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Metadata of a language that changed.
/// </summary>
/// <remarks>
/// A language is never deleted outright: <see cref="ChangeType.Deleted"/> on this payload means the
/// language was deactivated, and it can be activated again.
/// </remarks>
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
/// <c>last_modified</c>.
/// </remarks>
public sealed record SyncLanguageSystem
{
    /// <summary>The language's ID.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>The language's name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The language's codename.</summary>
    [JsonPropertyName("codename")]
    public required string Codename { get; init; }
}
