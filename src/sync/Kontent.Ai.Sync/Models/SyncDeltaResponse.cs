using System.Text.Json.Serialization;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents delta updates from a sync operation.
/// </summary>
internal sealed record SyncDeltaResponse : ISyncDeltaResponse
{
    /// <inheritdoc/>
    [JsonPropertyName("items")]
    public IReadOnlyList<SyncItem> Items { get; init; } = [];

    /// <inheritdoc/>
    [JsonPropertyName("types")]
    public IReadOnlyList<SyncType> Types { get; init; } = [];

    /// <inheritdoc/>
    [JsonPropertyName("languages")]
    public IReadOnlyList<SyncLanguage> Languages { get; init; } = [];

    /// <inheritdoc/>
    [JsonPropertyName("taxonomies")]
    public IReadOnlyList<SyncTaxonomy> Taxonomies { get; init; } = [];

    // Explicit interface implementations to expose as interfaces
    IReadOnlyList<ISyncItem> ISyncDeltaResponse.Items => Items;
    IReadOnlyList<ISyncType> ISyncDeltaResponse.Types => Types;
    IReadOnlyList<ISyncLanguage> ISyncDeltaResponse.Languages => Languages;
    IReadOnlyList<ISyncTaxonomy> ISyncDeltaResponse.Taxonomies => Taxonomies;
}
