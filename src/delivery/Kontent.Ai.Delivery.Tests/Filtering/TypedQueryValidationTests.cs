using System.Text.Json;
using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Generated;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.Filtering;

public sealed class TypedQueryValidationTests
{
    [Fact]
    public async Task Listing_UnresolvedModel_RejectsRepeatedExecutionBeforeCacheOrHttp()
    {
        var env = Guid.NewGuid().ToString();
        var mock = new MockHttpMessageHandler();
        var request = mock.When($"https://deliver.kontent.ai/{env}/items")
            .Respond("application/json", await ReadArticlePageAsync());
        var cache = new UnexpectedAccessCacheManager();
        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, delivery =>
        {
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock);
            delivery.UseCacheManager(_ => cache);
        });
        using var provider = services.BuildServiceProvider();
        var query = provider.GetRequiredService<IDeliveryClient>().GetItems<ArticleProjection>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteAsync());

        Assert.Equal(0, cache.Accesses);
        Assert.Equal(0, mock.GetMatchCount(request));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectedQuery_WithExplicitTypeFilter_CanRecoverAndPaginate(bool feed, bool inclusion)
    {
        var env = Guid.NewGuid().ToString();
        var url = $"https://deliver.kontent.ai/{env}/{(feed ? "items-feed" : "items")}";
        var mock = new MockHttpMessageHandler();
        var filter = inclusion ? "system.type[in]" : "system.type[eq]";
        var filterValue = inclusion ? "article,product" : "article";
        var first = mock.Expect(url).WithQueryString(filter, filterValue)
            .With(request => !request.RequestUri!.Query.Contains("skip="));
        if (feed)
            first.Respond(new Dictionary<string, string> { ["X-Continuation"] = "next" }, "application/json", await ReadArticlePageAsync());
        else
            first.Respond("application/json", await ReadArticlePageAsync(hasNext: true));

        var second = mock.Expect(url).WithQueryString(filter, filterValue);
        if (feed)
            second.WithHeaders("X-Continuation", "next");
        else
            second.WithQueryString("skip", "1");
        second.Respond("application/json", await ReadArticlePageAsync(skip: 1));

        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, delivery =>
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDeliveryClient>();
        Func<IItemsFilterBuilder, IItemsFilterBuilder> constrain = filters => inclusion
            ? filters.System("type").IsIn("article", "product")
            : filters.System("type").IsEqualTo("article");

        if (feed)
        {
            var query = client.GetItemsFeed<ArticleProjection>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteAsync("resume"));
            var result = await query.Where(constrain).ExecuteAsync();
            Assert.True(result.IsSuccess);
            Assert.Equal("Title of article 1", Assert.Single(result.Value.Items).Elements.Title);
            var next = await result.Value.FetchNextPageAsync();
            Assert.True(next!.IsSuccess);
            Assert.Equal("Title of article 2", Assert.Single(next.Value.Items).Elements.Title);
        }
        else
        {
            var query = client.GetItems<ArticleProjection>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => query.ExecuteAsync());
            var result = await query.Where(constrain).ExecuteAsync();
            Assert.True(result.IsSuccess);
            Assert.Equal("Title of article 1", Assert.Single(result.Value.Items).Elements.Title);
            var next = await result.Value.FetchNextPageAsync();
            Assert.True(next!.IsSuccess);
            Assert.Equal("Title of article 2", Assert.Single(next.Value.Items).Elements.Title);
        }

        Assert.Equal(1, mock.GetMatchCount(first));
        Assert.Equal(1, mock.GetMatchCount(second));
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeedEnumeration_UnresolvedModel_RejectsFirstPageWithoutHttp(bool asPages)
    {
        var env = Guid.NewGuid().ToString();
        var mock = new MockHttpMessageHandler();
        var request = mock.When($"https://deliver.kontent.ai/{env}/items-feed")
            .Respond("application/json", await ReadArticlePageAsync());
        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, delivery =>
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        using var provider = services.BuildServiceProvider();
        var enumeration = provider.GetRequiredService<IDeliveryClient>().GetItemsFeed<ArticleProjection>().EnumerateAsync();

        if (asPages)
        {
            await using var iterator = enumeration.AsPages().GetAsyncEnumerator();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await iterator.MoveNextAsync());
        }
        else
        {
            await using var iterator = enumeration.GetAsyncEnumerator();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await iterator.MoveNextAsync());
        }

        Assert.Equal(0, mock.GetMatchCount(request));
    }

    [Fact]
    public async Task Listing_RegisteredModel_KeepsExactlyOneAutomaticFilterAcrossPages()
    {
        var env = Guid.NewGuid().ToString();
        var url = $"https://deliver.kontent.ai/{env}/items";
        var mock = new MockHttpMessageHandler();
        mock.Expect(url).WithExactQueryString("system.type[eq]=article")
            .Respond("application/json", await ReadArticlePageAsync(hasNext: true));
        mock.Expect(url).WithExactQueryString("skip=1&system.type[eq]=article")
            .Respond("application/json", await ReadArticlePageAsync(skip: 1));
        var services = new ServiceCollection();
        services.AddSingleton<ITypeProvider, GeneratedTypeProvider>();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, delivery =>
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        using var provider = services.BuildServiceProvider();

        var first = await provider.GetRequiredService<IDeliveryClient>().GetItems<Models.ContentTypes.Article>().ExecuteAsync();
        Assert.True(first.IsSuccess);
        Assert.True((await first.Value.FetchNextPageAsync())!.IsSuccess);
        mock.VerifyNoOutstandingExpectation();
    }

    private static async Task<string> ReadArticlePageAsync(int skip = 0, bool hasNext = false)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine("Fixtures", "DeliveryClient", "items.json")));
        var articles = document.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("system").GetProperty("type").GetString() == "article");
        return JsonSerializer.Serialize(new
        {
            items = articles.Skip(skip).Take(1).ToArray(),
            modular_content = new { },
            pagination = new { skip, limit = 1, count = 1, next_page = hasNext ? "https://deliver.kontent.ai/environment/items?skip=1" : "" }
        });
    }

    public sealed record ArticleProjection
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }
    }

    private sealed class UnexpectedAccessCacheManager : IDeliveryCacheManager
    {
        public int Accesses { get; private set; }
        public CacheStorageMode StorageMode
        {
            get
            {
                Accesses++;
                throw new InvalidOperationException("Unexpected cache access.");
            }
        }

        public Task<CacheResult<T>?> GetOrSetAsync<T>(string cacheKey, Func<CancellationToken, Task<CacheEntry<T>?>> factory,
            TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            Accesses++;
            throw new InvalidOperationException("Unexpected cache access.");
        }

        public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
