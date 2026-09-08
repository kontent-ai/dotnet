using System.Net;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.Caching;

public sealed class InvalidateAssetTests
{
    private static readonly Guid AssetId = Guid.Parse("f6daed1f-3f3b-4036-a9c7-9519359b9601");
    private const string EnvironmentId = "975bf280-fd91-488c-994c-2f04416e5ee3";
    private const string BaseUrl = $"https://deliver.kontent.ai/{EnvironmentId}";

    [Fact]
    public async Task InvalidateAssetAsync_InvalidatesTheAssetKey_EveryItemUsingIt_AndTheListScope()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{BaseUrl}/assets/hero_image/used-in")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .WithQueryString("system.language[in]", "default,french")
            .Respond("application/json", await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory, "Fixtures", "DeliveryClient", "used_in.json")));
        var manager = new RecordingCacheManager();

        var result = await manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId);

        Assert.True(result);
        var keys = Assert.Single(manager.Invalidations);
        Assert.Equal(
            [
                $"asset_{AssetId}",
                "item_coffee_beverages_explained",
                "item_coffee_processing_techniques",
                "item_donate_with_us",
                "item_on_roasts",
                "item_origins_of_arabica_bourbon",
                "item_which_brewing_fits_you_",
                DeliveryCacheDependencies.ItemsListScope,
            ],
            keys);
    }

    [Fact]
    public async Task InvalidateAssetAsync_InvalidatesNothing_WhenTheLookupFails()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{BaseUrl}/assets/hero_image/used-in").Respond(HttpStatusCode.InternalServerError);
        var manager = new RecordingCacheManager();

        await Assert.ThrowsAsync<DeliveryRequestException>(
            () => manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId));

        Assert.Empty(manager.Invalidations);
    }

    [Fact]
    public async Task InvalidateAssetAsync_ReturnsWhatTheManagerReturns()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{BaseUrl}/assets/hero_image/used-in")
            .Respond("application/json", await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory, "Fixtures", "DeliveryClient", "used_in.json")));
        var manager = new RecordingCacheManager { Outcome = false };

        Assert.False(await manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId));
    }

    [Fact]
    public async Task InvalidateAssetAsync_IncludesLanguagesFromEveryPage()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect($"{BaseUrl}/languages")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .Respond("application/json", LanguagePage("default", skip: 0, hasNext: true));
        mock.Expect($"{BaseUrl}/languages")
            .WithQueryString("skip", "1")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .Respond("application/json", LanguagePage("french", skip: 1));
        mock.Expect($"{BaseUrl}/assets/hero_image/used-in")
            .WithQueryString("system.language[in]", "default,french")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .Respond("application/json", UsagePage("french_article", "french"));
        var manager = new RecordingCacheManager();

        Assert.True(await manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId));

        Assert.Contains("item_french_article", Assert.Single(manager.Invalidations));
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task InvalidateAssetAsync_InvalidatesNothing_WhenASecondUsagePageFails()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect($"{BaseUrl}/languages").Respond("application/json", LanguagePage("default"));
        mock.Expect($"{BaseUrl}/assets/hero_image/used-in")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(UsagePage("article", "default"), System.Text.Encoding.UTF8, "application/json"),
                Headers = { { "X-Continuation", "next-page" } },
            });
        mock.Expect($"{BaseUrl}/assets/hero_image/used-in")
            .WithHeaders("X-Continuation", "next-page")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .WithQueryString("system.language[in]", "default")
            .Respond(HttpStatusCode.InternalServerError);
        var manager = new RecordingCacheManager();

        await Assert.ThrowsAsync<DeliveryRequestException>(() => manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId));

        Assert.Empty(manager.Invalidations);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidateAssetAsync_InvalidatesNothing_WhenALanguagePageFails(bool secondPage)
    {
        var mock = new MockHttpMessageHandler();
        if (secondPage)
        {
            mock.Expect($"{BaseUrl}/languages").Respond("application/json", LanguagePage("default", hasNext: true));
        }
        mock.Expect($"{BaseUrl}/languages").Respond(HttpStatusCode.InternalServerError);
        var manager = new RecordingCacheManager();

        await Assert.ThrowsAsync<DeliveryRequestException>(() => manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId));

        Assert.Empty(manager.Invalidations);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task InvalidateAssetAsync_InvalidatesNothing_WhenNoLanguagesAreReturned()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect($"{BaseUrl}/languages").Respond("application/json", """
            { "languages": [], "pagination": { "skip": 0, "limit": 0, "count": 0, "next_page": "" } }
            """);
        var manager = new RecordingCacheManager();

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId));

        Assert.Empty(manager.Invalidations);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task InvalidateAssetAsync_Cancellation_InvalidatesNothing()
    {
        var mock = new MockHttpMessageHandler();
        var manager = new RecordingCacheManager();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InvalidateAssetAsync(CreateClient(mock), "hero_image", AssetId, cancellation.Token));

        Assert.Empty(manager.Invalidations);
    }

    private static string LanguagePage(string codename, int skip = 0, bool hasNext = false) => $$"""
        {
          "languages": [{ "system": { "id": "11111111-1111-1111-1111-111111111111", "name": "{{codename}}", "codename": "{{codename}}" } }],
          "pagination": { "skip": {{skip}}, "limit": 1, "count": 1, "next_page": "{{(hasNext ? $"{BaseUrl}/languages?skip={skip + 1}" : "")}}" }
        }
        """;

    private static string UsagePage(string codename, string language) => $$"""
        { "items": [{ "system": { "id": "22222222-2222-2222-2222-222222222222", "name": "{{codename}}", "codename": "{{codename}}", "language": "{{language}}", "type": "article", "collection": "default", "workflow": "default", "workflow_step": "published", "last_modified": "2026-09-08T00:00:00Z" } }] }
        """;

    private static IDeliveryClient CreateClient(MockHttpMessageHandler mock)
    {
        mock.When($"{BaseUrl}/languages")
            .WithHeaders("X-KC-Wait-For-Loading-New-Content", "True")
            .Respond("application/json", """
                {
                  "languages": [
                    { "system": { "id": "11111111-1111-1111-1111-111111111111", "name": "Default", "codename": "default" } },
                    { "system": { "id": "22222222-2222-2222-2222-222222222222", "name": "French", "codename": "french" } }
                  ],
                  "pagination": { "skip": 0, "limit": 2, "count": 2, "next_page": "" }
                }
                """);
        var services = new ServiceCollection();
        services.AddDeliveryClient(
            new DeliveryOptions { EnvironmentId = EnvironmentId, EnableResilience = false },
            d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        return services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();
    }

    private sealed class RecordingCacheManager : IDeliveryCacheManager
    {
        public List<string[]> Invalidations { get; } = [];
        public bool Outcome { get; init; } = true;

        public CacheStorageMode StorageMode => CacheStorageMode.HydratedObject;

        public Task<CacheResult<T>?> GetOrSetAsync<T>(string cacheKey, Func<CancellationToken, Task<CacheEntry<T>?>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            where T : class
            => throw new NotSupportedException();

        public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
        {
            Invalidations.Add(dependencyKeys);
            return Task.FromResult(Outcome);
        }
    }
}
