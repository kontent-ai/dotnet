using System.Text.Json;
using Kontent.Ai.Delivery.ContentTypes.Element;
using Kontent.Ai.Delivery.Serialization.Converters;
using Kontent.Ai.Delivery.SharedModels;

namespace Kontent.Ai.Delivery.Tests.Serialization;

public class ContentElementDictionaryConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    [Fact]
    public void Read_InvalidRootToken_ThrowsJsonException()
    {
        var json = "[1, 2, 3]";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<IReadOnlyDictionary<string, ContentElement>>(json, Options));
    }

    [Fact]
    public void Read_ValidElements_HydratesCodenameFromKey()
    {
        var json = """
        {
            "title": { "type": "text", "name": "Title" },
            "body": { "type": "rich_text", "name": "Body" }
        }
        """;

        var result = JsonSerializer.Deserialize<IReadOnlyDictionary<string, ContentElement>>(json, Options)!;

        Assert.Equal(2, result.Count);
        Assert.Equal("title", result["title"].Codename);
        Assert.Equal("body", result["body"].Codename);
    }

    // Writing exists for the distributed cache, which stores and re-reads these dictionaries. Anything the
    // write drops is data a second node silently loses, so the assertion is on the round trip, not the call.
    [Fact]
    public void Write_RoundTripsEveryElementKind()
    {
        var dict = new Dictionary<string, ContentElement>
        {
            ["title"] = new ContentElement { Type = "text", Name = "Title", Codename = "title" },
            ["category"] = new TaxonomyElement
            {
                Type = "taxonomy",
                Name = "Category",
                Codename = "category",
                TaxonomyGroup = "categories"
            },
            ["rating"] = new MultipleChoiceElement
            {
                Type = "multiple_choice",
                Name = "Rating",
                Codename = "rating",
                Options = [new MultipleChoiceOption { Name = "Good", Codename = "good" }]
            }
        } as IReadOnlyDictionary<string, ContentElement>;

        var json = JsonSerializer.Serialize(dict, Options);
        var result = JsonSerializer.Deserialize<IReadOnlyDictionary<string, ContentElement>>(json, Options)!;

        Assert.Equal("text", result["title"].Type);
        Assert.Equal("title", result["title"].Codename);
        Assert.Equal("categories", Assert.IsType<TaxonomyElement>(result["category"]).TaxonomyGroup);
        Assert.Equal("good", Assert.IsType<MultipleChoiceElement>(result["rating"]).Options[0].Codename);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ContentElementConverter());
        options.Converters.Add(new ContentElementDictionaryConverter());
        return options;
    }
}
