using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Represents the type of change that occurred to a synchronized entity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChangeType>))]
public enum ChangeType
{
    /// <summary>
    /// The entity was added or modified.
    /// </summary>
    [JsonStringEnumMemberName("changed")]
    Changed,

    /// <summary>
    /// The entity was deleted.
    /// </summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted
}
