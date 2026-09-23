using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Attributes;

namespace KontentAiModels;

// Models the published samples query without showing their definition.

[ContentTypeCodename("page")]
public partial record Page
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("subpages")]
    public IEnumerable<IEmbeddedContent>? Subpages { get; init; }
}

[ContentTypeCodename("navigation_item")]
public partial record NavigationItem
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("subitems")]
    public IEnumerable<IEmbeddedContent>? Subitems { get; init; }
}

[ContentTypeCodename("product")]
public partial record Product
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

[ContentTypeCodename("video")]
public partial record Video
{
    [JsonPropertyName("video_id")]
    public string? VideoId { get; init; }
}
