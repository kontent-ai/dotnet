using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Tests.Models.ContentTypes;
using KontentAiModels;
using Kontent.Ai.Delivery.ContentItems.RichText.Resolution;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/structure-in-rte
/// </summary>
public class StructureInRichText
{
    private readonly IHtmlResolver _resolver = new HtmlResolverBuilder().Build();

    [Fact]
    public void ImplementLinkResolver()
    {
        // DocSection: structure_in_rte_implement_link_resolver
        // Define URL patterns for resolving content item links by content type
        // Available placeholders: {codename}, {type}, {urlslug}, {id}
        var linkResolver = DefaultResolvers.UrlPatternResolver(new Dictionary<string, string>
        {
            ["article"] = "/articles/{urlslug}"
        });

        // For other means of resolving links, see SDK docs:
        // https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/rich-text-customization.md#content-item-link-resolvers
        // EndDocSection

        Assert.NotNull(linkResolver);
    }

    [Fact]
    public void ImplementResolver()
    {
        // DocSection: structure_in_rte_implement_resolver
        // Build an HTML resolver for embedded content items and content item links
        var resolver = new HtmlResolverBuilder()
            // Render embedded Tweet components
            .WithContentResolver<Tweet>(tweet =>
                $"<blockquote class=\"twitter-tweet\" data-lang=\"en\" data-theme=\"{tweet.Elements.Theme?.FirstOrDefault()?.Codename}\"><a href=\"{tweet.Elements.TweetLink}\"></a></blockquote>")
            // Render embedded YouTube video components
            .WithContentResolver<Video>(video =>
                $"<iframe src=\"https://youtube.com/embed/{video.Elements.VideoId}\"></iframe>")
            .Build();
        // EndDocSection

        Assert.NotNull(resolver);
    }

    [Fact]
    public void RegisterLinkResolver()
    {
        var linkResolver = DefaultResolvers.UrlPatternResolver(new Dictionary<string, string> { ["article"] = "/articles/{urlslug}" });

        // DocSection: structure_in_rte_register_link_resolver
        // Build an HTML resolver with the content item link resolver from the previous step
        var resolver = new HtmlResolverBuilder()
            .WithContentItemLinkResolver(linkResolver)
            .Build();
        // EndDocSection

        Assert.NotNull(resolver);
    }

    [Fact]
    public void RegisterResolver()
    {
        var services = new ServiceCollection();
        var resolver = new HtmlResolverBuilder().Build();

        // DocSection: structure_in_rte_register_resolver
        // Register the resolver as a singleton in the service collection
        services.AddSingleton<IHtmlResolver>(resolver);

        // Alternatively, resolvers can be instantiated directly and passed to ToHtmlAsync
        // without DI registration — useful for per-controller or per-service resolution
        // EndDocSection

        Assert.Single(services);
    }

    [Fact]
    public async Task RetrieveArticle()
    {
        var client = SampleClient.Create("CodeSamples/simple_article.json");

        // DocSection: structure_in_rte_retrieve_article
        var result = await client.GetItem<SimpleArticle>("my_article").ExecuteAsync();

        if (result.IsSuccess && result.Value.Elements.Body is { } body)
        {
            // Resolve the rich text body to HTML
            // _resolver can be a local variable or resolved from DI (IHtmlResolver)
            string html = await body.ToHtmlAsync(_resolver);
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
        Assert.Contains("Ethiopia", await result.Value.Elements.Body!.ToHtmlAsync(_resolver));
    }
}
