// Published with the using directives the model generator emits, some of which this project has implicitly.
#pragma warning disable IDE0005
// DocSection: strongly_typed_models_definition
#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Attributes;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.RichText;
using Kontent.Ai.Delivery.SharedModels;

namespace KontentAiModels;

// Identifies this record as the Homepage content type for the SDK's type resolution
[ContentTypeCodename("homepage")]
public partial record Homepage
{
    // Stores the codename of each element for use in queries and switch expressions
    public const string BodyTextCodename = "body_text";
    public const string HeadlineCodename = "headline";
    public const string PictureCodename = "picture";

    public const string ContentTypeCodename = "homepage";

    [JsonPropertyName("body_text")]
    public RichTextContent? BodyText { get; init; }

    [JsonPropertyName("headline")]
    public string? Headline { get; init; }

    [JsonPropertyName("picture")]
    public IEnumerable<Asset>? Picture { get; init; }
}
// EndDocSection
