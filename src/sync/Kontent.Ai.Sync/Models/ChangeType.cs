using System.Text.Json.Serialization;
using Kontent.Ai.Sync.Models;

namespace Kontent.Ai.Sync;

/// <summary>
/// Represents the type of change that occurred to a synchronized entity.
/// </summary>
[JsonConverter(typeof(ChangeTypeConverter))]
public enum ChangeType
{
    /// <summary>
    /// The entity was added or modified.
    /// </summary>
    Changed,

    /// <summary>
    /// The entity was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// A change type this version of the SDK does not recognize. The Sync API may add change types without
    /// notice; the entry still identifies the entity it describes.
    /// </summary>
    Unknown,
}
