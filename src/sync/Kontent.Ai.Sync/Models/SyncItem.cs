using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents a delta update for a content item.
/// </summary>
internal sealed record SyncItem : ISyncItem
{
    /// <inheritdoc/>
    [JsonPropertyName("change_type")]
    public ChangeType ChangeType { get; init; }

    /// <inheritdoc/>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}
