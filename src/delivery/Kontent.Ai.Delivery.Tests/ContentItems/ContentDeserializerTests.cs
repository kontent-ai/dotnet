using System.Text.Json;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;

namespace Kontent.Ai.Delivery.Tests.ContentItems;

public class ContentDeserializerTests
{
    private static readonly JsonSerializerOptions Options = RefitSettingsProvider.CreateDefaultJsonSerializerOptions();

    private const string ValidJson = """
    {
        "system": {
            "id": "00000000-0000-0000-0000-000000000001",
            "name": "Test",
            "codename": "test",
            "type": "article",
            "collection": "default",
            "workflow": "default",
            "workflow_step": "published",
            "language": "en-US",
            "last_modified": "2024-01-01T00:00:00Z",
            "sitemap_locations": []
        },
        "elements": {}
    }
    """;

    private static JsonElement Element => JsonSerializer.Deserialize<JsonElement>(ValidJson);

    [Fact]
    public void Deserialize_Generic_ReturnsContentItemOfThatModel()
    {
        var sut = new ContentDeserializer(Options);

        // Typed as ContentItem<IDynamicElements> by the compiler - the caller casts nothing.
        ContentItem<IDynamicElements> result = sut.Deserialize<IDynamicElements>(Element);

        Assert.Equal("test", result.System.Codename);
    }

    [Fact]
    public void Deserialize_Generic_CapturesRawItemJson()
    {
        var sut = new ContentDeserializer(Options);

        var result = sut.Deserialize<IDynamicElements>(Element);

        Assert.True(((IRawContentItem)result).RawItemJson.HasValue);
    }

    [Fact]
    public void Deserialize_RuntimeType_NullModelType_ThrowsArgumentNullException()
    {
        var sut = new ContentDeserializer(Options);

        Assert.Throws<ArgumentNullException>(() => sut.Deserialize(Element, null!));
    }

    [Fact]
    public void Deserialize_RuntimeType_ReturnsContentItemOfTheResolvedModel()
    {
        var sut = new ContentDeserializer(Options);

        var result = sut.Deserialize(Element, typeof(IDynamicElements));

        var contentItem = Assert.IsType<ContentItem<IDynamicElements>>(result);
        Assert.Equal("test", contentItem.System.Codename);
    }
}
