using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Tests.Models.ContentTypes;
using KontentAiModels;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// Source of the .NET samples that read content items, across several folders of
/// https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net
/// </summary>
public class GetContent
{
    [Fact]
    public async Task ApplyAssetRenditions()
    {
        var client = SampleClient.Create("CodeSamples/article_with_hero_image.json");

        // DocSection: apply_asset_renditions
        // Retrieves a content item
        var result = await client.GetItem<Article>("my_article").ExecuteAsync();

        if (result.IsSuccess)
        {
            // Gets the image from an asset element named Hero Image
            var imageWithRendition = result.Value.Elements.HeroImage?
                .SingleOrDefault(x => x.Name == "construction-insurance-header.jpg");

            if (imageWithRendition is not null
                && imageWithRendition.Renditions.TryGetValue("default", out var rendition))
            {
                // Combines the original image URL with the asset rendition query
                var assetUrl = $"{imageWithRendition.Url}?{rendition.Query}";
            }
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAllArticles()
    {
        var client = SampleClient.Create("DeliveryClient/articles.json");

        // DocSection: getting_content_filter_items
        // Gets all articles
        // Note: When using strongly typed models with [ContentTypeCodename("article")],
        // the system.type filter is added automatically for GetItems<Article>()
        var result = await client.GetItems<Article>().ExecuteAsync();

        if (result.IsSuccess)
        {
            IReadOnlyList<IContentItem<Article>> items = result.Value.Items;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAllItems()
    {
        var client = SampleClient.Create("DeliveryClient/items.json");

        // DocSection: getting_content_get_items
        // Gets all content items
        // Note: Without type parameter, GetItems() returns either runtime typed items or falls back to DynamicElements
        // see https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/models.md#dynamic-content-access for details
        var result = await client.GetItems().ExecuteAsync();

        if (result.IsSuccess)
        {
            var items = result.Value.Items;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetOrderedItems()
    {
        var client = SampleClient.Create("DeliveryClient/articles.json");

        // DocSection: getting_content_order_items
        // Gets the 3 latest articles ordered by their last modified time
        // Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        var result = await client.GetItems<Article>()
            .OrderBy("system.last_modified", OrderingMode.Descending)
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
    public async Task GetItemInLanguage()
    {
        var client = SampleClient.Create("DeliveryClient/coffee_beverages_explained.json");

        // DocSection: getting_localized_content_language
        // Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        // Gets a specific article in Spanish (with language fallbacks enabled by default)
        var result = await client.GetItem<Article>("about_us")
            .WithLanguage("es-ES")
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            Article item = result.Value.Elements;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemByLocalizedUrlSlug()
    {
        var client = SampleClient.Create("DeliveryClient/articles.json");

        // DocSection: getting_localized_content_url_slug
        // Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        // Filters all articles to find the Spanish variant by its URL slug
        var result = await client.GetItems<Article>()
            .WithLanguage("es-ES")
            .Where(item => item.Element("url_pattern").IsEqualTo("acerda-de-nosotros"))
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            IReadOnlyList<IContentItem<Article>> items = result.Value.Items;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetItemsWithoutLanguageFallbacks()
    {
        var client = SampleClient.Create("DeliveryClient/items.json");

        // DocSection: language_fallbacks_ignore
        // Gets content items in Spanish without following language fallbacks
        var result = await client.GetItems()
            .WithLanguage("es-ES", LanguageFallbackMode.Disabled)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            var items = result.Value.Items;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetNavigation()
    {
        var client = SampleClient.Create("CodeSamples/navigation_item.json");

        // DocSection: managing_navigation_articles_slugs
        // Gets navigation items and their linked items
        // Tip: Create strongly typed models according to https://kontent.ai/learn/develop/build-apps/generate-models/net
        var rootResult = await client
            .GetItem<NavigationItem>("root_navigation_item")
            .Depth(5)
            .ExecuteAsync();

        if (rootResult.IsSuccess)
        {
            var root = rootResult.Value.Elements;
        }
        // EndDocSection

        Assert.True(rootResult.IsSuccess);
    }

    [Fact]
    public async Task GetLatestContent()
    {
        var client = SampleClient.Create("DeliveryClient/coffee_beverages_explained.json");

        // DocSection: using_webhooks_get_latest_content
        // Gets a content item; asks the API to return the latest content if it changed since the last request
        // Tip: Create strongly typed models according to https://kontent.ai/learn/net-strong-types
        var result = await client.GetItem<Article>("my_article")
            .WaitForLoadingNewContent(true)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            Article item = result.Value.Elements;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetArticleWithAuthor()
    {
        var client = SampleClient.Create("CodeSamples/simple_article.json");

        // DocSection: linked_content_get_article_with_author
        // Gets a specific article and its linked items
        // Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        var result = await client.GetItem<SimpleArticle>("the_origin_of_coffee")
            .Depth(1)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            SimpleArticle item = result.Value.Elements;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetPageWithSubpages()
    {
        var client = SampleClient.Create("CodeSamples/page.json");

        // DocSection: linked_content_get_page_with_subpages
        // Gets a specific page, its subpages, and linked items
        // Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        var result = await client.GetItem<Page>("insurance_listing")
            .Depth(1)
            .ExecuteAsync();

        if (result.IsSuccess)
        {
            Page item = result.Value.Elements;
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RetrieveStronglyTypedModel()
    {
        var client = SampleClient.Create("CodeSamples/homepage.json");

        // DocSection: strongly_typed_models_retrieve
        // Gets a content item by codename and maps it to the strongly typed model
        var result = await client.GetItem<Homepage>("hello_caas_world").ExecuteAsync();

        if (result.IsSuccess)
        {
            var homepage = result.Value.Elements;
            // Use homepage
            // Console.WriteLine(homepage.Headline);
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
    }

    // These two show how the client is registered, so they declare it themselves and only compile: the
    // placeholders would fail options validation if they ran.
    internal async Task PreviewContent()
    {
        // DocSection: preview_content_get_article
        // Or register it through DI with services.AddDeliveryClient()
        using var client = DeliveryClient.Create(new DeliveryOptions { EnvironmentId = "your-environment-id" }
            .UsePreviewApi("your-preview-api-key"));

        // Gets the latest version of a content item
        // Tip: Generate models via https://github.com/kontent-ai/dotnet/tree/main/src/model-generator
        var result = await client.GetItem<Article>("my_article").ExecuteAsync();

        if (result.IsSuccess)
        {
            Article item = result.Value.Elements;
        }
        // EndDocSection
    }

    internal async Task SecurePublicAccess()
    {
        // DocSection: securing_public_access_get_article
        // Or register it through DI with services.AddDeliveryClient()
        using var client = DeliveryClient.Create(new DeliveryOptions { EnvironmentId = "your-environment-id" }
            .UseProductionApi("your-delivery-api-key"));

        // Gets a specific content item
        // Tip: Create strongly typed models according to https://kontent.ai/learn/net-strong-types
        var result = await client.GetItem<Article>("my_article").ExecuteAsync();

        if (result.IsSuccess)
        {
            Article item = result.Value.Elements;
        }
        // EndDocSection
    }
}
