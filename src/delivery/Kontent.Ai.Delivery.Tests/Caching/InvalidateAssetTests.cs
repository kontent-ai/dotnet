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
        // A partial list would evict some items and leave the rest stale while reporting success; the walk
        // throws instead, so the webhook fails and is delivered again.
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

    private static IDeliveryClient CreateClient(MockHttpMessageHandler mock)
    {
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
