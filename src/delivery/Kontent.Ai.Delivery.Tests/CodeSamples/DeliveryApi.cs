using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Tests.Models.ContentTypes;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/delivery-api
/// </summary>
public class DeliveryApi
{
    private readonly List<object> processed = [];

    [Fact]
    public async Task GetAssetUsedIn()
    {
        var client = SampleClient.Create("DeliveryClient/used_in.json");

        // DocSection: delivery_api_get_asset_used_in
        // Enumerates all parent content items of type "article" for asset 'my_asset'
        await foreach (var usedInItem in client.GetAssetUsedIn("my_asset")
            .Where(item => item.System("type").IsEqualTo("article"))
            .EnumerateAsync())
        {
            // Do something with the parent content item, e.g. update cache
            ProcessUsedInItem(usedInItem);
        }
        // EndDocSection

        Assert.NotEmpty(processed);

        void ProcessUsedInItem(object item) => processed.Add(item);
    }

    [Fact]
    public async Task GetContentElement()
    {
        var client = SampleClient.Create("CodeSamples/element_title.json");

        // DocSection: delivery_api_get_element
        // Gets the model of a specific element within a specific content type
        var result = await client.GetContentElement("article", "title").ExecuteAsync();

        if (result.IsSuccess)
        {
            IContentElement element = result.Value;
            Console.WriteLine($"Name: {element.Name}");
            Console.WriteLine($"Type: {element.Type}");
            Console.WriteLine($"Codename: {element.Codename}");
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItem()
    {
        var client = SampleClient.Create("DeliveryClient/coffee_beverages_explained.json");

        // DocSection: delivery_api_get_item
        // Gets a strongly typed article
        // Tip: Create strongly typed models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        var result = await client.GetItem<Article>("my_article").ExecuteAsync();

        if (result.IsSuccess)
        {
            Article item = result.Value.Elements;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemUsedIn()
    {
        var client = SampleClient.Create("DeliveryClient/used_in.json");

        // DocSection: delivery_api_get_item_used_in
        // Enumerates all parent content items of type "article" for item 'my_article'
        await foreach (var usedInItem in client.GetItemUsedIn("my_article")
            .Where(item => item.System("type").IsEqualTo("article"))
            .EnumerateAsync())
        {
            // Do something with the parent content item, e.g. update cache
            ProcessUsedInItem(usedInItem);
        }
        // EndDocSection

        Assert.NotEmpty(processed);

        void ProcessUsedInItem(object item) => processed.Add(item);
    }

    [Fact]
    public async Task GetItems()
    {
        var client = SampleClient.Create("DeliveryClient/articles.json");

        // DocSection: delivery_api_get_items
        // Gets 3 articles ordered by the "Post date" element
        // Note: When using strongly typed models with [ContentTypeCodename("article")],
        // the system.type filter is added automatically for GetItems<Article>()
        var result = await client.GetItems<Article>()
            .OrderBy("elements.post_date", OrderingMode.Descending)
            .Limit(3)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            IReadOnlyList<IContentItem<Article>> items = result.Value.Items;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemsFeed()
    {
        var client = SampleClient.Create("DeliveryClient/articles_feed.json");

        // DocSection: delivery_api_get_items_feed
        // Enumerates all articles in the project
        // Note: When using strongly typed models with [ContentTypeCodename("article")],
        // the system.type filter is added automatically for GetItemsFeed<Article>()
        await foreach (var article in client.GetItemsFeed<Article>().EnumerateAsync())
        {
            // Do something with the content item, e.g. update cache
            ProcessContentItem(article.Elements);
        }
        // EndDocSection

        Assert.NotEmpty(processed);

        void ProcessContentItem(Article article) => processed.Add(article);
    }

    [Fact]
    public async Task GetLanguages()
    {
        var client = SampleClient.Create("DeliveryClient/languages.json");

        // DocSection: delivery_api_get_languages
        // Gets 3 languages
        var result = await client.GetLanguages()
            .Limit(3)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            IReadOnlyList<ILanguage> languages = result.Value.Languages;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetTaxonomyGroup()
    {
        var client = SampleClient.Create("DeliveryClient/taxonomies_personas.json");

        // DocSection: delivery_api_get_taxonomy_group
        // Gets a specific taxonomy group
        var result = await client.GetTaxonomy("personas").ExecuteAsync();

        if (result.IsSuccess)
        {
            ITaxonomyGroup taxonomy = result.Value;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetTaxonomyGroups()
    {
        var client = SampleClient.Create("DeliveryClient/taxonomies_multiple.json");

        // DocSection: delivery_api_get_taxonomy_groups
        // Gets 3 taxonomy groups
        var result = await client.GetTaxonomies()
            .Limit(3)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            IReadOnlyList<ITaxonomyGroup> taxonomies = result.Value.Taxonomies;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetContentType()
    {
        var client = SampleClient.Create("CodeSamples/type_article.json");

        // DocSection: delivery_api_get_type
        // Gets a specific content type
        var result = await client.GetType("article").ExecuteAsync();

        if (result.IsSuccess)
        {
            IContentType type = result.Value;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetContentTypes()
    {
        var client = SampleClient.Create("DeliveryClient/types_accessory.json");

        // DocSection: delivery_api_get_types
        // Gets 3 content types
        var result = await client.GetTypes()
            .Limit(3)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            IReadOnlyList<IContentType> types = result.Value.Types;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }
}
