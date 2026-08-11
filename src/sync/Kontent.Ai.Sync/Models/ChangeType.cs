using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// Represents the type of change that occurred to a synchronized entity.
/// </summary>
/// <remarks>
/// Each member states its wire name rather than relying on a naming policy. The converter named here takes
/// precedence over the one the SDK configures, and it carries no policy - so serializing a delta wrote
/// <c>"Changed"</c> where the API sends <c>"changed"</c>, and a consumer who re-sent what they had read
/// produced something the API does not accept. Deserialization was unaffected, being case-insensitive,
/// which is why nothing failed on the way in.
/// </remarks>
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
