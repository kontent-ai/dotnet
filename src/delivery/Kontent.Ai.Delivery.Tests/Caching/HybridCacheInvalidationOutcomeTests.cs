using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Kontent.Ai.Delivery.Tests.Caching;

/// <summary>
/// What <see cref="IDeliveryCacheManager.InvalidateAsync"/> answers when the distributed tier is not there
/// to receive the invalidation. A webhook handler retries on <c>false</c>, so <c>true</c> has to mean every
/// node will see it.
/// </summary>
public sealed class HybridCacheInvalidationOutcomeTests
{
    private static readonly DeliveryCacheOptions Options = new() { DefaultExpiration = TimeSpan.FromMinutes(5) };

    [Fact]
    public async Task InvalidateAsync_ReturnsFalse_WhenTheDistributedWriteFails()
    {
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, Options);
        await PrimeAsync(manager, "article", "item_article");

        store.Down = true;

        Assert.False(await manager.InvalidateAsync(["item_article"]));
    }

    [Fact]
    public async Task InvalidateAsync_ReturnsFalse_WhileTheDistributedCircuitBreakerIsOpen()
    {
        // A failed read opens the breaker; FusionCache then skips the distributed tier without throwing,
        // so a healthy store is not enough - the invalidation written now never gets there.
        var store = new SwitchableDistributedCache { Down = true };
        using var manager = FusionCacheManager.CreateHybrid(store, Options);
        await PrimeAsync(manager, "article", "item_article");

        store.Down = false;

        Assert.False(await manager.InvalidateAsync(["item_article"]));
    }

    [Fact]
    public async Task InvalidateAsync_ReturnsFalse_WhenTheBackplaneCannotPublish()
    {
        using var manager = FusionCacheManager.CreateHybrid(new SwitchableDistributedCache(), Options, backplane: new UnreachableBackplane());
        await PrimeAsync(manager, "article", "item_article");

        Assert.False(await manager.InvalidateAsync(["item_article"]));
    }

    [Fact]
    public async Task InvalidateAsync_StillClearsTheMemoryTier_WhenItReturnsFalse()
    {
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, Options);
        await PrimeAsync(manager, "article", "item_article");
        store.Down = true;
        Assert.False(await manager.InvalidateAsync(["item_article"]));

        var refetched = await manager.GetOrSetAsync("article", _ => Task.FromResult<CacheEntry<Payload>?>(new(new Payload("fresh"), ["item_article"])));

        Assert.True(refetched!.FromFactory);
        Assert.Equal("fresh", refetched.Value.Text);
    }

    [Fact]
    public async Task InvalidateAsync_ReturnsTrue_OnceTheStoreIsBackAndTheBreakerHasClosed()
    {
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(
            store,
            new DeliveryCacheOptions
            {
                DefaultExpiration = TimeSpan.FromMinutes(5),
                ConfigureFusionCacheOptions = o => ((FusionCacheOptions)o).DistributedCacheCircuitBreakerDuration = TimeSpan.FromMilliseconds(50),
            });
        await PrimeAsync(manager, "article", "item_article");
        store.Down = true;
        Assert.False(await manager.InvalidateAsync(["item_article"]));

        store.Down = false;
        await Task.Delay(100);

        Assert.True(await manager.InvalidateAsync(["item_article"]));
    }

    [Fact]
    public async Task InvalidateAsync_InMemoryMode_IsNotAffectedByBreakerTracking()
    {
        var services = new ServiceCollection().AddMemoryCache().BuildServiceProvider();
        using var manager = FusionCacheManager.CreateMemory(services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), Options);
        await PrimeAsync(manager, "article", "item_article");

        Assert.True(await manager.InvalidateAsync(["item_article"]));
    }

    private static async Task PrimeAsync(FusionCacheManager manager, string key, string dependency)
    {
        var entry = await manager.GetOrSetAsync(key, _ => Task.FromResult<CacheEntry<Payload>?>(new(new Payload("cached"), [dependency])));
        Assert.True(entry!.FromFactory);
    }

    private sealed record Payload(string Text);

    /// <summary>
    /// An in-memory distributed cache that can be taken down mid-test, failing the way a lost Redis
    /// connection does.
    /// </summary>
    private sealed class SwitchableDistributedCache : IDistributedCache
    {
        private readonly IDistributedCache _inner =
            new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider().GetRequiredService<IDistributedCache>();

        public bool Down { get; set; }

        private IDistributedCache Live => Down ? throw new IOException("The distributed cache is unavailable.") : _inner;

        public byte[]? Get(string key) => Live.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Live.GetAsync(key, token);
        public void Refresh(string key) => Live.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => Live.RefreshAsync(key, token);
        public void Remove(string key) => Live.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => Live.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Live.Set(key, value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Live.SetAsync(key, value, options, token);
    }

    /// <summary>A backplane whose every publish fails, as it does when the channel is gone.</summary>
    private sealed class UnreachableBackplane : IFusionCacheBackplane
    {
        public void Subscribe(BackplaneSubscriptionOptions options) { }
        public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) => ValueTask.CompletedTask;
        public void Unsubscribe() { }
        public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;
        public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => throw new IOException("The backplane is unavailable.");
        public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => throw new IOException("The backplane is unavailable.");
    }
}
