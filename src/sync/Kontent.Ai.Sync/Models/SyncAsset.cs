using System.Text.Json;
using System.Text.Json.Serialization;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents a delta update for an asset.
/// </summary>
internal sealed class SyncAsset : ISyncAsset
{
    /// <inheritdoc/>
    [JsonPropertyName("change_type")]
    public ChangeType ChangeType { get; init; }

    /// <inheritdoc/>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}
