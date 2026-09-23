using KontentAiModels;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/filter-content
/// </summary>
public class FilterContent
{
    [Fact]
    public async Task GetItemById()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_item_by_id
        var result = await client.GetItems()
            .Where(item => item.System("id").IsEqualTo("2f7288a1-cfc8-47be-9bf1-b1d312f7da18"))
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemsByCodenameIn()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_codename_in
        // Gets three items by their codenames. The codenames are unique per environment.
        var result = await client.GetItems()
            .Where(item => item.System("codename").IsIn("delivery_api", "get_content", "hello_world"))
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemsByDateTime()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_datetime
        // Note: Date & time element values are provided by users and stored with minute precision.
        // The system.last_modified value reflects last content change to an item and is stored with second precision.

        // Gets items modified after May 9 2020, 9 am UTC (using DateTime overload)
        var result = await client.GetItems()
            .Where(item => item.System("last_modified")
                .IsGreaterThan(new DateTime(2020, 5, 9, 9, 0, 0, DateTimeKind.Utc)))
            .ExecuteAsync();

        // Gets items released at or after May 9 2020, 7 am UTC (using string overload)
        var result2 = await client.GetItems()
            .Where(item => item.Element("release_date")
                .IsGreaterThanOrEqualTo("2020-05-09T07:00:00Z"))
            .ExecuteAsync();

        // Gets items modified before May 5 2020 UTC. Last match would be at 2020-05-04T23:59:59.
        // Date-only string — no time component appended by the SDK.
        var result3 = await client.GetItems()
            .Where(item => item.System("last_modified").IsLessThan("2020-05-05"))
            .ExecuteAsync();

        // Gets items released at or before May 5 2020 10:30 am UTC (using DateTime overload)
        var result4 = await client.GetItems()
            .Where(item => item.Element("release_date")
                .IsLessThanOrEqualTo(new DateTime(2020, 5, 5, 10, 30, 0, DateTimeKind.Utc)))
            .ExecuteAsync();
        // EndDocSection

        Assert.All([result, result2, result3, result4], r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task GetItemsByLinkedItem()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_linked_item
        // Gets items where the 'navigation' linked items element contains 'my_page'.
        var result = await client.GetItems()
            .Where(item => item.Element("navigation").Contains("my_page"))
            .ExecuteAsync();

        // Gets items linked to at least Jane, John, or both.
        var result2 = await client.GetItems()
            .Where(item => item.Element("author").ContainsAny("jane_doe", "john_wick"))
            .ExecuteAsync();

        // Gets pages linking travel insurance as their subpage.
        var result3 = await client.GetItems()
            .Where(item => item.Element("subpages").Contains("travel_insurance"))
            .ExecuteAsync();

        // Gets pages linking at least travel insurance, car insurance, or both as their subpage.
        var result4 = await client.GetItems()
            .Where(item => item.Element("subpages").ContainsAny("travel_insurance", "car_insurance"))
            .ExecuteAsync();
        // EndDocSection

        Assert.All([result, result2, result3, result4], r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task GetItemsByRange()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_range
        // Note: Date & time element values are provided by users and stored with minute precision. The system.last_modified value reflects last content change to an item and is stored with ms precision.

        // Gets items modified between May 5, 2020 10:30 UTC and May 7, 2020 7:00 UTC (inclusive)
        // Using string overload — values passed through as-is to the API
        var result = await client.GetItems()
            .Where(item => item.System("last_modified")
                .IsWithinRange("2020-05-05T10:30:00Z", "2020-05-07T07:00:00Z"))
            .ExecuteAsync();

        // Equivalent using DateTime overload
        var result2 = await client.GetItems()
            .Where(item => item.System("last_modified")
                .IsWithinRange(
                    new DateTime(2020, 5, 5, 10, 30, 0, DateTimeKind.Utc),
                    new DateTime(2020, 5, 7, 7, 0, 0, DateTimeKind.Utc)))
            .ExecuteAsync();
        // EndDocSection

        Assert.All([result, result2], r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task GetItemsByString()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_string
        // Gets items whose Title element value equals "Hello World"
        var result = await client.GetItems()
            .Where(item => item.Element("title").IsEqualTo("Hello World"))
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemsByTaxonomyTerm()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_taxonomy_term
        // Note: Filters work with codenames of the tags.

        // Gets items tagged with one specific tag
        var result1 = await client.GetItems()
            .Where(item => item.Element("tags").Contains("kontent_ai"))
            .ExecuteAsync();

        // Gets items tagged with all specified tags
        var result2 = await client.GetItems()
            .Where(item => item.Element("tags").ContainsAll("kontent_ai", "cms"))
            .ExecuteAsync();

        // Gets items tagged with at least one tag from the list
        var result3 = await client.GetItems()
            .Where(item => item.Element("tags").ContainsAny("headless", "cms"))
            .ExecuteAsync();
        // EndDocSection

        Assert.All([result1, result2, result3], r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task GetItemsByUrlSlug()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_by_url_slug
        // Gets items in default language with the URL slug element equal to 'sample-url-slug'
        var result = await client.GetItems()
            .Where(item => item.Element("url_slug").IsEqualTo("sample-url-slug"))
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemsOfType()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_of_type
        // Gets items based on the "product" type
        var result = await client.GetItems()
            .Where(item => item.System("type").IsEqualTo("product"))
            .ExecuteAsync();

        // Note: When using generated models with [ContentTypeCodename("product")] attribute,
        // the type filter is added automatically and this manual filter is not needed.
        var result2 = await client.GetItems<Product>()
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess && result2.IsSuccess);
    }

    [Fact]
    public async Task GetItemsOfTypes()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_items_of_types
        // Gets items based on the types Product, Article, and News
        var result = await client.GetItems()
            .Where(item => item.System("type").IsIn("product", "article", "news"))
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetProductWithinRating()
    {
        var client = SampleClient.Create("CodeSamples/no_items.json");

        // DocSection: filtering_get_product_within_rating
        // Gets items whose rating is at least 6.5 and at most 9
        var result = await client.GetItems()
            .Where(item => item.Element("product_rating").IsWithinRange(6.5, 9))
            .ExecuteAsync();
        // EndDocSection

        Assert.True(result.IsSuccess);
    }
}
