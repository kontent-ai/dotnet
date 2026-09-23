// Published with the using directives the model generator emits, some of which this project has implicitly.
#pragma warning disable IDE0005
// DocSection: linked_content_simple_article_model
#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Attributes;
using Kontent.Ai.Delivery.ContentItems.RichText;

// Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator

namespace KontentAiModels;

// Identifies this record as the Simple Article content type for the SDK's type resolution
[ContentTypeCodename("simple_article")]
public partial record SimpleArticle
{
    // Stores the codename of each element for use in queries and switch expressions
    public const string TitleCodename = "title";
    public const string BodyCodename = "body";
    public const string AuthorCodename = "author";

    public const string ContentTypeCodename = "simple_article";

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("body")]
    public RichTextContent? Body { get; init; }

    [JsonPropertyName("author")]
    public IEnumerable<IEmbeddedContent>? Author { get; init; }
}
// EndDocSection
#pragma warning restore IDE0005
