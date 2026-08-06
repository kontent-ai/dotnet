using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync;

/// <summary>
/// One entry in the sync feed: what changed, when, and the metadata describing it.
/// </summary>
/// <remarks>
/// Every delta object the API returns has this envelope, whatever kind of entity it describes -
/// so it is modelled once and parameterised by the payload. What sits inside <c>data</c> is not
/// uniform: a content item carries its collection, language, type and workflow, while a language
/// carries three fields and no timestamp of its own.
/// </remarks>
/// <typeparam name="TData">The metadata this kind of change carries.</typeparam>
public sealed record SyncChange<TData> where TData : class
{
    /// <summary>
    /// Whether the entity was modified or removed since the last synchronization.
    /// </summary>
    [JsonPropertyName("change_type")]
    public required ChangeType ChangeType { get; init; }

    /// <summary>
    /// When the change occurred in the Delivery API, in UTC.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The affected entity's metadata.
    /// </summary>
    [JsonPropertyName("data")]
    public TData? Data { get; init; }
}
