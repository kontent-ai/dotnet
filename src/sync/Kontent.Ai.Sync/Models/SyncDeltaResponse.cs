using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.Models;

internal sealed record SyncDeltaResponse : ISyncDeltaResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<SyncChange<SyncItemData>> Items { get; init; } = [];

    [JsonPropertyName("types")]
    public IReadOnlyList<SyncChange<SyncTypeData>> Types { get; init; } = [];

    [JsonPropertyName("languages")]
    public IReadOnlyList<SyncChange<SyncLanguageData>> Languages { get; init; } = [];

    [JsonPropertyName("taxonomies")]
    public IReadOnlyList<SyncChange<SyncTaxonomyData>> Taxonomies { get; init; } = [];
}
