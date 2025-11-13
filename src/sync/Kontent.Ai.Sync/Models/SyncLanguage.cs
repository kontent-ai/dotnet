using System.Text.Json;
using System.Text.Json.Serialization;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents a delta update for a language.
/// </summary>
internal sealed record SyncLanguage : ISyncLanguage
{
    /// <inheritdoc/>
    [JsonPropertyName("change_type")]
    public ChangeType ChangeType { get; init; }

    /// <inheritdoc/>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}
