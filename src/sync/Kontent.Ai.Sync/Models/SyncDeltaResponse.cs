using System.Text.Json.Serialization;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Models;

/// <summary>
/// Represents delta updates from a sync operation.
/// </summary>
internal sealed class SyncDeltaResponse : ISyncDeltaResponse
{
    /// <inheritdoc/>
    [JsonPropertyName("items")]
    public IReadOnlyList<ISyncItem> Items { get; init; } = Array.Empty<ISyncItem>();

    /// <inheritdoc/>
    [JsonPropertyName("assets")]
    public IReadOnlyList<ISyncAsset> Assets { get; init; } = Array.Empty<ISyncAsset>();

    /// <inheritdoc/>
    [JsonPropertyName("types")]
    public IReadOnlyList<ISyncType> Types { get; init; } = Array.Empty<ISyncType>();

    /// <inheritdoc/>
    [JsonPropertyName("languages")]
    public IReadOnlyList<ISyncLanguage> Languages { get; init; } = Array.Empty<ISyncLanguage>();

    /// <inheritdoc/>
    [JsonPropertyName("taxonomies")]
    public IReadOnlyList<ISyncTaxonomy> Taxonomies { get; init; } = Array.Empty<ISyncTaxonomy>();
}
