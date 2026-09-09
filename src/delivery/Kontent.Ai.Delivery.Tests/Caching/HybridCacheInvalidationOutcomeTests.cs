using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Kontent.Ai.Delivery.Tests.Caching;

/// <summary>
/// Outcomes of distributed invalidations and purges, including circuit-breaker skips.
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
        using var manager = FusionCacheManager.CreateHybrid(new SwitchableDistributedCache(), Options, backplane: new SwitchableBackplane { Down = true });
        await PrimeAsync(manager, "article", "item_article");

        Assert.False(await manager.InvalidateAsync(["item_article"]));
    }

    [Fact]
    public async Task GetOrSetAsync_ServesTheMemoryTier_WhileTheStoreIsDown()
    {
        // The tag options are shared with reads: FusionCache checks an entry's tags on a hit with them, so
        // strictness there would turn every memory hit during an outage into an exception.
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, Options);
        await PrimeAsync(manager, "article", "item_article");

        store.Down = true;
        var hit = await manager.GetOrSetAsync("article", _ => Task.FromResult<CacheEntry<Payload>?>(new(new Payload("fresh"), ["item_article"])));

        Assert.False(hit!.FromFactory);
        Assert.Equal("cached", hit.Value.Text);
    }

    [Fact]
    public async Task InvalidateAsync_ClearsEveryKeyLocally_WhenTheStoreIsDown()
    {
        // FusionCache stops at the first tag that throws; the manager must not, or the second item stays.
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, Options);
        await PrimeAsync(manager, "article", "item_article");
        await PrimeAsync(manager, "page", "item_page");
        store.Down = true;

        Assert.False(await manager.InvalidateAsync(["item_article", "item_page"]));

        var article = await manager.GetOrSetAsync("article", _ => Task.FromResult<CacheEntry<Payload>?>(new(new Payload("fresh"), ["item_article"])));
        var page = await manager.GetOrSetAsync("page", _ => Task.FromResult<CacheEntry<Payload>?>(new(new Payload("fresh"), ["item_page"])));
        Assert.True(article!.FromFactory);
        Assert.True(page!.FromFactory);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_Throws_WhenTheDistributedWriteFails(bool allowFailSafe)
    {
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, PurgeOptions);
        await PrimeAsync(manager, "article", "item_article");
        store.Down = true;

        var exception = await Assert.ThrowsAsync<FusionCacheDistributedCacheException>(() => manager.PurgeAsync(allowFailSafe));

        Assert.IsType<IOException>(exception.InnerException);
        await PrimeAsync(manager, "article", "item_article");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_Throws_WhileTheDistributedCircuitBreakerIsOpen(bool allowFailSafe)
    {
        var store = new SwitchableDistributedCache { Down = true };
        using var manager = FusionCacheManager.CreateHybrid(store, PurgeOptions);
        await PrimeAsync(manager, "article", "item_article");
        store.Down = false;
        var attempts = store.SetAttempts;

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PurgeAsync(allowFailSafe));

        Assert.Equal(attempts, store.SetAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_Throws_WhenTheBackplaneCannotPublish(bool allowFailSafe)
    {
        var backplane = new SwitchableBackplane();
        using var manager = FusionCacheManager.CreateHybrid(new SwitchableDistributedCache(), PurgeOptions, backplane: backplane);
        await PrimeAsync(manager, "article", "item_article");
        backplane.Down = true;

        var exception = await Assert.ThrowsAsync<FusionCacheBackplaneException>(() => manager.PurgeAsync(allowFailSafe));

        Assert.IsType<IOException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_Throws_WhileTheBackplaneCircuitBreakerIsOpen(bool allowFailSafe)
    {
        var backplane = new SwitchableBackplane { Down = true };
        using var manager = FusionCacheManager.CreateHybrid(new SwitchableDistributedCache(), PurgeOptions, backplane: backplane);
        await PrimeAsync(manager, "article", "item_article");
        backplane.Down = false;
        var attempts = backplane.PublishAttempts;

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PurgeAsync(allowFailSafe));

        Assert.Equal(attempts, backplane.PublishAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_ReachesANewNode_AfterTheStoreRecovers(bool allowFailSafe)
    {
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, new DeliveryCacheOptions
        {
            ConfigureFusionCacheOptions = o =>
            {
                var options = (FusionCacheOptions)o;
                options.EnableAutoRecovery = false;
                options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromMilliseconds(50);
            }
        });
        await PrimeAsync(manager, "article", "item_article");
        store.Down = true;
        await Assert.ThrowsAsync<FusionCacheDistributedCacheException>(() => manager.PurgeAsync(allowFailSafe));
        store.Down = false;
        await Task.Delay(100);

        await manager.PurgeAsync(allowFailSafe);

        using var other = FusionCacheManager.CreateHybrid(store, PurgeOptions);
        await PrimeAsync(other, "article", "item_article");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_InMemoryMode_InvalidatesEntries(bool allowFailSafe)
    {
        using var services = new ServiceCollection().AddMemoryCache().BuildServiceProvider();
        using var manager = FusionCacheManager.CreateMemory(services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), Options);
        await PrimeAsync(manager, "article", "item_article");

        await manager.PurgeAsync(allowFailSafe);

        await PrimeAsync(manager, "article", "item_article");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurgeAsync_Publishes_AfterTheBackplaneRecovers(bool allowFailSafe)
    {
        var backplane = new SwitchableBackplane();
        using var manager = FusionCacheManager.CreateHybrid(new SwitchableDistributedCache(), new DeliveryCacheOptions
        {
            ConfigureFusionCacheOptions = o =>
            {
                var options = (FusionCacheOptions)o;
                options.EnableAutoRecovery = false;
                options.BackplaneCircuitBreakerDuration = TimeSpan.FromMilliseconds(50);
            }
        }, backplane: backplane);
        await PrimeAsync(manager, "article", "item_article");
        backplane.Down = true;
        await Assert.ThrowsAsync<FusionCacheBackplaneException>(() => manager.PurgeAsync(allowFailSafe));
        backplane.Down = false;
        var attempts = backplane.PublishAttempts;
        await Task.Delay(100);

        await manager.PurgeAsync(allowFailSafe);

        Assert.Equal(attempts + 1, backplane.PublishAttempts);
    }

    [Fact]
    public async Task PurgeAsync_WithCanceledToken_DoesNotWrite()
    {
        var store = new SwitchableDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, PurgeOptions);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.PurgeAsync(cancellationToken: cancellation.Token));

        Assert.Equal(0, store.SetAttempts);
    }

    [Fact]
    public async Task PurgeAsync_AfterDisposal_Throws()
    {
        var manager = FusionCacheManager.CreateHybrid(new SwitchableDistributedCache(), PurgeOptions);
        manager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.PurgeAsync());
    }

    private static readonly DeliveryCacheOptions PurgeOptions = new()
    {
        ConfigureFusionCacheOptions = o =>
        {
            var options = (FusionCacheOptions)o;
            options.EnableAutoRecovery = false;
            options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromMinutes(1);
            options.BackplaneCircuitBreakerDuration = TimeSpan.FromMinutes(1);
            options.TagsDefaultEntryOptions.AllowBackgroundDistributedCacheOperations = true;
            options.TagsDefaultEntryOptions.AllowBackgroundBackplaneOperations = true;
        }
    };

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
        public int SetAttempts { get; private set; }

        private IDistributedCache Live => Down ? throw new IOException("The distributed cache is unavailable.") : _inner;

        public byte[]? Get(string key) => Live.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Live.GetAsync(key, token);
        public void Refresh(string key) => Live.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => Live.RefreshAsync(key, token);
        public void Remove(string key) => Live.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => Live.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            SetAttempts++;
            Live.Set(key, value, options);
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetAttempts++;
            return Live.SetAsync(key, value, options, token);
        }
    }

    private sealed class SwitchableBackplane : IFusionCacheBackplane
    {
        public bool Down { get; set; }
        public int PublishAttempts { get; private set; }
        public void Subscribe(BackplaneSubscriptionOptions options) { }
        public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) => ValueTask.CompletedTask;
        public void Unsubscribe() { }
        public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;
        public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
        {
            PublishAttempts++;
            if (Down) throw new IOException("The backplane is unavailable.");
        }
        public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
        {
            Publish(message, options, token);
            return ValueTask.CompletedTask;
        }
    }
}
