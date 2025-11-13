using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the type of change that occurred to a synchronized entity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeType
{
    /// <summary>
    /// The entity was added or modified.
    /// </summary>
    Changed,

    /// <summary>
    /// The entity was deleted.
    /// </summary>
    Deleted
}
