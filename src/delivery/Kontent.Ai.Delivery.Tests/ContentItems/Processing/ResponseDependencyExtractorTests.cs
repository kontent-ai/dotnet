using System.Text.Json;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Processing;

namespace Kontent.Ai.Delivery.Tests.ContentItems.Processing;

/// <summary>
/// The keys come from the wire, so every case here reads through <c>IDynamicElements</c> - a model that maps
/// nothing. What it yields is what every model gets.
/// </summary>
public sealed class ResponseDependencyExtractorTests
{
    private static readonly JsonSerializerOptions JsonOptions = RefitSettingsProvider.CreateDefaultJsonSerializerOptions();

    private const string AssetId = "f6daed1f-3f3b-4036-a9c7-9519359b9601";
    private const string ImageId = "11111111-1111-1111-1111-111111111111";
    private const string LinkedAssetId = "22222222-2222-2222-2222-222222222222";
    private const string ComponentAssetId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void Extract_TracksTheItemAndItsType()
    {
        var keys = Extract(Item("article", "post", elements: "{}"));

        Assert.Contains("item_article", keys);
        Assert.Contains("type_post", keys);
    }

    [Fact]
    public void Extract_TracksAnAssetElementByTheIdInItsUrl()
    {
        var keys = Extract(Item("article", "post", elements: $$"""
            {
              "teaser": { "type": "asset", "name": "Teaser", "value": [
                { "name": "a.jpg", "type": "image/jpeg", "size": 1, "description": "", "url": "https://assets.kontent.ai/975bf280-fd91-488c-994c-2f04416e5ee3/{{AssetId}}/a.jpg" },
                { "name": "odd.jpg", "type": "image/jpeg", "size": 1, "description": "", "url": "not a url" }
              ] }
            }
            """));

        Assert.Contains($"asset_{AssetId}", keys);
        Assert.Single(keys, k => k.StartsWith("asset_", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_TracksATaxonomyElementByItsGroup()
    {
        var keys = Extract(Item("article", "post", elements: """
            { "tags": { "type": "taxonomy", "name": "Tags", "taxonomy_group": "personas", "value": [ { "name": "Coffee", "codename": "coffee" } ] } }
            """));

        Assert.Contains("taxonomy_personas", keys);
        Assert.DoesNotContain("taxonomy_coffee", keys);
    }

    [Fact]
    public void Extract_TracksEverythingARichTextElementRefersTo()
    {
        var keys = Extract(Item("article", "post", elements: $$"""
            {
              "body": {
                "type": "rich_text", "name": "Body",
                "images": { "{{ImageId}}": { "image_id": "{{ImageId}}", "description": null, "url": "https://assets.kontent.ai/x/{{ImageId}}/i.png", "width": 1, "height": 1 } },
                "links": { "44444444-4444-4444-4444-444444444444": { "codename": "linked_article", "type": "post", "url_slug": "linked" } },
                "modular_content": [ "inline_component" ],
                "value": "<p>See <a data-asset-id=\"{{LinkedAssetId}}\" href=\"https://assets.kontent.ai/x/{{LinkedAssetId}}/f.pdf\">the file</a></p><figure data-asset-id=\"{{ImageId}}\"><img src=\"i.png\" data-asset-id=\"{{ImageId}}\"></figure>"
              }
            }
            """));

        Assert.Contains($"asset_{ImageId}", keys);
        Assert.Contains($"asset_{LinkedAssetId}", keys);
        Assert.Contains("item_linked_article", keys);
        Assert.Contains("item_inline_component", keys);
    }

    [Fact]
    public void Extract_TracksLinkedItemsBeyondTheRequestedDepth()
    {
        // Only "near" came back in modular_content; "far" is past the depth, but a change to it still
        // changes what this response should say.
        var keys = Extract(
            Item("article", "post", elements: """
                { "related": { "type": "modular_content", "name": "Related", "value": [ "near", "far" ] } }
                """),
            modularContent: $$"""{ "near": {{Item("near", "post", elements: "{}")}} }""");

        Assert.Contains("item_near", keys);
        Assert.Contains("item_far", keys);
    }

    [Fact]
    public void Extract_WalksModularContent_AndSkipsAComponentsOwnKey()
    {
        var keys = Extract(
            Item("article", "post", elements: "{}"),
            modularContent: $$"""
                {
                  "author": {{Item("author", "person", elements: """{ "tags": { "type": "taxonomy", "taxonomy_group": "roles", "value": [] } }""")}},
                  "n1a2b3c4_component": {{Component("n1a2b3c4_component", "callout", elements: $$"""
                      { "image": { "type": "asset", "value": [ { "url": "https://assets.kontent.ai/x/{{ComponentAssetId}}/c.png" } ] } }
                      """)}}
                }
                """);

        Assert.Contains("item_author", keys);
        Assert.Contains("type_person", keys);
        Assert.Contains("taxonomy_roles", keys);

        Assert.DoesNotContain("item_n1a2b3c4_component", keys);
        Assert.Contains("type_callout", keys);
        Assert.Contains($"asset_{ComponentAssetId}", keys);
    }

    [Fact]
    public void Extract_ReadsEveryRootOfAListing()
    {
        var response = JsonSerializer.Deserialize<DeliveryItemListingResponse<IDynamicElements>>(
            $$"""{ "items": [ {{Item("a", "post", "{}")}}, {{Item("b", "page", "{}")}} ], "modular_content": {}, "pagination": { "skip": 0, "limit": 0, "count": 2, "next_page": "" } }""",
            JsonOptions)!;

        var keys = ResponseDependencyExtractor.Extract(response.Items, response.ModularContent);

        Assert.Contains("item_a", keys);
        Assert.Contains("item_b", keys);
        Assert.Contains("type_post", keys);
        Assert.Contains("type_page", keys);
    }

    private static string[] Extract(string item, string modularContent = "{}")
    {
        var response = JsonSerializer.Deserialize<DeliveryItemResponse<IDynamicElements>>(
            $$"""{ "item": {{item}}, "modular_content": {{modularContent}} }""",
            JsonOptions)!;

        return ResponseDependencyExtractor.Extract([response.Item], response.ModularContent);
    }

    private static string Item(string codename, string type, string elements) => $$"""
        {
          "system": { "id": "{{Guid.NewGuid()}}", "name": "{{codename}}", "codename": "{{codename}}", "language": "en-US", "type": "{{type}}", "collection": "default", "sitemap_locations": [], "last_modified": "2026-01-01T00:00:00Z", "workflow": "default", "workflow_step": "published" },
          "elements": {{elements}}
        }
        """;

    // A component's system block has no workflow, which is how the SDK tells it from an item.
    private static string Component(string codename, string type, string elements) => $$"""
        {
          "system": { "id": "{{Guid.NewGuid()}}", "name": "{{codename}}", "codename": "{{codename}}", "language": "en-US", "type": "{{type}}", "collection": "default", "sitemap_locations": [], "last_modified": "2026-01-01T00:00:00Z" },
          "elements": {{elements}}
        }
        """;
}
