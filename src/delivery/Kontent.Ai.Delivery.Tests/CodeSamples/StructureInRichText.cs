using Kontent.Ai.Delivery.Abstractions;
using KontentAiModels;
using Kontent.Ai.Delivery.ContentItems.RichText.Resolution;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Encodings.Web;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/structure-in-rte
/// </summary>
public class StructureInRichText
{
    private readonly IHtmlResolver _resolver = new HtmlResolverBuilder().Build();

    [Fact]
    public async Task ImplementLinkResolver()
    {
        // DocSection: structure_in_rte_implement_link_resolver
        BlockResolver<IContentItemLink> linkResolver = async (link, resolveChildren) =>
        {
            // The link text with its formatting, already rendered to HTML, so don't encode it
            var text = await resolveChildren(link.Children);

            // Metadata is null when the linked item isn't available, such as when it's deleted or unpublished
            var metadata = link.Metadata;
            var slug = metadata is { UrlSlug.Length: > 0 } ? metadata.UrlSlug : metadata?.Codename;

            var url = metadata?.ContentTypeCodename switch
            {
                "article" => $"/articles/{slug}",
                "product" => $"/products/{slug}",
                _ => null
            };

            return url is null ? text : $"<a href=\"{HtmlEncoder.Default.Encode(url)}\">{text}</a>";
        };
        // EndDocSection

        var client = SampleClient.Create("CodeSamples/article_with_links.json");
        var article = (await client.GetItem<SimpleArticle>("coffee_filters").ExecuteAsync()).Value;
        var html = await article.Elements.Body!.ToHtmlAsync(new HtmlResolverBuilder().WithContentItemLinkResolver(linkResolver).Build());

        Assert.Equal(
            "<p>The grounds stay in the <a href=\"/products/paper_filters\">filter</a>, while the <a href=\"/articles/which-brewing-fits-you\"><strong>brewed coffee</strong></a> drips into a carafe.</p>",
            html);
    }

    [Fact]
    public async Task ImplementResolver()
    {
        // DocSection: structure_in_rte_implement_resolver
        // The returned markup is inserted as is, so encode element values with HtmlEncoder
        var resolver = new HtmlResolverBuilder()
            .WithContentResolver<YoutubeVideo>(video =>
            {
                var videoId = HtmlEncoder.Default.Encode(video.Elements.VideoId ?? "");
                var title = HtmlEncoder.Default.Encode(video.Elements.Title ?? "YouTube video");

                return $"""
                    <iframe src="https://www.youtube.com/embed/{videoId}" title="{title}" width="560" height="315"
                            referrerpolicy="strict-origin-when-cross-origin" allowfullscreen></iframe>
                    """;
            })
            .Build();
        // EndDocSection

        var client = SampleClient.Create("CodeSamples/article_with_embeds.json");
        var article = (await client.GetItem<SimpleArticle>("brewing_at_home").ExecuteAsync()).Value;
        var html = await article.Elements.Body!.ToHtmlAsync(resolver);

        Assert.Contains("<iframe src=\"https://www.youtube.com/embed/dQw4w9WgXcQ&quot; onload=&quot;alert(1)\" title=\"French press &amp; pour-over\"", html);
    }

    [Fact]
    public void RegisterLinkResolver()
    {
        BlockResolver<IContentItemLink> linkResolver = (_, _) => ValueTask.FromResult(string.Empty);

        // DocSection: structure_in_rte_register_link_resolver
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
            // _resolver can be a local variable or resolved from DI (IHtmlResolver)
            string html = await body.ToHtmlAsync(_resolver);
        }
        // EndDocSection

        Assert.True(result.IsSuccess);
        Assert.Contains("Ethiopia", await result.Value.Elements.Body!.ToHtmlAsync(_resolver));
    }
}
