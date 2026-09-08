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
        using var cancelled = new CancellationTokenSource();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var resolver = new HtmlResolverBuilder()
            .WithTextNodeResolver(async (node, _) =>
            {
                if (node.Text == "first")
                {
                    if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
                    await resume.Task;
                }
                return node.Text;
            })
            .Build();

        var dead = RenderAsync(cancelled.Token);
        var live = RenderAsync(CancellationToken.None);
        try
        {
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancelled.Cancel();
        }
        finally
        {
            resume.SetResult();
        }

        Assert.Equal("<p>first<strong>second</strong></p>", await live);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dead);

        async Task<string> RenderAsync(CancellationToken token) => await resolver.ResolveAsync(Nested(), token);
    }
}
