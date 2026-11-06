using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.Models;

internal sealed record SyncDeltaResponse : ISyncDeltaResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<SyncChange<SyncItemData>> Items { get; init; }

    [JsonPropertyName("types")]
    public required IReadOnlyList<SyncChange<SyncTypeData>> Types { get; init; }

    [JsonPropertyName("languages")]
    public required IReadOnlyList<SyncChange<SyncLanguageData>> Languages { get; init; }

    [JsonPropertyName("taxonomies")]
    public required IReadOnlyList<SyncChange<SyncTaxonomyData>> Taxonomies { get; init; }
}
