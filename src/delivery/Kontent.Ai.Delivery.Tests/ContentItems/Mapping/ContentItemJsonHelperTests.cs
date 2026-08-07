using System.Text.Json;
using Kontent.Ai.Delivery.ContentItems.Mapping;

namespace Kontent.Ai.Delivery.Tests.ContentItems.Mapping;

/// <summary>
/// Components used to be told apart by the shape of their generated codename, which an authored codename
/// could coincide with - and the item that collided was then never given a dependency key, so a webhook
/// for it evicted nothing. These pin the structural signal that replaced it.
/// </summary>
public class ContentItemJsonHelperTests
{
    // A content item as the Delivery API returns it, workflow and all.
    private const string ContentItem = """
        {
          "system": {
            "id": "117cdfae-52cf-4885-b271-66aef6825612",
            "name": "Coffee processing techniques",
            "codename": "coffee_processing_techniques",
            "language": "en-US",
            "type": "article",
            "last_modified": "2019-03-27T13:13:35.312Z",
            "workflow": "default",
            "workflow_step": "published"
          }
        }
        """;

    // A component: generated codename, name equal to its id, and no workflow of its own.
    private const string Component = """
        {
          "system": {
            "id": "d7610e80-9a93-01ef-284c-c1dfdbcf43ee",
            "name": "d7610e80-9a93-01ef-284c-c1dfdbcf43ee",
            "codename": "d7610e80_9a93_01ef_284c_c1dfdbcf43ee",
            "language": "en-US",
            "type": "tweet",
            "last_modified": "2019-09-18T10:58:38.9172599Z"
          }
        }
        """;

    [Fact]
    public void IsComponent_ContentItem_IsFalse() =>
        Assert.False(ContentItemJsonHelper.IsComponent(Parse(ContentItem)));

    [Fact]
    public void IsComponent_Component_IsTrue() =>
        Assert.True(ContentItemJsonHelper.IsComponent(Parse(Component)));

    [Fact]
    public void IsComponent_AuthoredCodenameShapedLikeAComponent_IsStillAnItem()
    {
        // "Product SKU 0123 Blue" — the codename the old rule misread, but the workflow is right there.
        var json = ContentItem
            .Replace("coffee_processing_techniques", "product_sku_0123_blue")
            .Replace("Coffee processing techniques", "Product SKU 0123 Blue");

        Assert.False(ContentItemJsonHelper.IsComponent(Parse(json)));
    }

    [Fact]
    public void IsComponent_NoSystemBlock_IsNotTreatedAsAComponent()
    {
        // Nothing to go on, so track it: a stray key is cheaper than a missed invalidation.
        Assert.False(ContentItemJsonHelper.IsComponent(Parse("""{ "elements": {} }""")));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
