using Kontent.Ai.Delivery.ContentItems.RichText;
using Kontent.Ai.Delivery.ContentItems.RichText.Blocks;
using Kontent.Ai.Delivery.ContentItems.RichText.Resolution;

namespace Kontent.Ai.Delivery.Tests.RichText;

public sealed class HtmlResolverCancellationTests
{
    // <p>first<strong>second</strong></p>: the second text node sits one level down, where the top-level
    // loop never looks.
    private static RichTextContent Nested() => new(
    [
        new HtmlNode("p", new Dictionary<string, string>(),
        [
            new TextNode("first"),
            new HtmlNode("strong", new Dictionary<string, string>(), [new TextNode("second")]),
        ]),
    ]);

    [Fact]
    public async Task ResolveAsync_StopsBelowTheTopLevel_OnceTheTokenIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var visited = new List<string>();
        var resolver = new HtmlResolverBuilder()
            .WithTextNodeResolver((node, _) =>
            {
                visited.Add(node.Text);
                if (node.Text == "first") cts.Cancel();
                return ValueTask.FromResult(node.Text);
            })
            .Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await resolver.ResolveAsync(Nested(), cts.Token));

        Assert.Equal(["first"], visited);
    }

    [Fact]
    public async Task ResolveAsync_RendersEveryLevel_WhenNothingIsCancelled()
    {
        var resolver = new HtmlResolverBuilder().Build();

        Assert.Equal("<p>first<strong>second</strong></p>", await resolver.ResolveAsync(Nested()));
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentRenders_KeepTheirOwnTokens()
    {
        // The resolver is shared; a token belongs to one render, not to the resolver.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var resolver = new HtmlResolverBuilder().Build();

        var live = resolver.ResolveAsync(Nested()).AsTask();
        var dead = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await resolver.ResolveAsync(Nested(), cancelled.Token));

        Assert.Equal("<p>first<strong>second</strong></p>", await live);
        await dead;
    }
}
