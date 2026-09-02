using System.Reflection;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Delivery.Tests.Builders.Configuration;

public class DeliveryClientCreateTests
{
    private const string EnvironmentId = "550cec62-90a6-4ab3-b3e4-3d0bb4c04f5c";
    private const string TestPreviewApiKey = "preview.api.key";
    private const string TestSecureApiKey = "secure.api.key";

    [Fact]
    public void Create_NullDelegate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DeliveryClient.Create((Action<IDeliveryClientBuilder>)null!));
    }

    [Fact]
    public void Create_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DeliveryClient.Create((DeliveryOptions)null!));
    }

    [Fact]
    public async Task Create_ProductionApi_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d => d.Options.Configure(o => o.EnvironmentId = EnvironmentId));

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IDeliveryClient>(client);
    }

    [Fact]
    public async Task Create_PreviewApi_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d => d.Options.Configure(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UsePreviewApi(TestPreviewApiKey);
        }));

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_SecureAccess_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d => d.Options.Configure(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UseProductionApi(TestSecureApiKey);
        }));

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_FromOptionsInstance_CopiesTheValues()
    {
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId }.UsePreviewApi(TestPreviewApiKey);

        await using var client = DeliveryClient.Create(options);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithDisabledResilience_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d => d.Options.Configure(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.EnableResilience = false;
        }));

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithTypeProvider_CreatesClient()
    {
        var typeProvider = new TestTypeProvider();

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.AddSingleton<ITypeProvider>(typeProvider);
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithLoggerFactory_CreatesClient()
    {
        using var loggerFactory = LoggerFactory.Create(b => { });

        await using var client = DeliveryClient.Create(d =>
        {
            d.Services.AddSingleton(loggerFactory);
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithMemoryCache_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30));
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithMemoryCacheDefaultExpiration_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.UseMemoryCache();
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_WithPreviewApiAndMemoryCache_CacheManagerStoresAndReads()
    {
        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o =>
            {
                o.EnvironmentId = EnvironmentId;
                o.UsePreviewApi(TestPreviewApiKey);
            });
            d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30));
        });

        var cacheManager = GetCacheManager(client);
        Assert.NotNull(cacheManager);

        var factoryCalled = false;
        var cached = await cacheManager.GetOrSetAsync("preview-test", _ =>
        {
            factoryCalled = true;
            return Task.FromResult<CacheEntry<string>?>(
                new CacheEntry<string>("value", ["item_preview"]));
        });

        Assert.True(factoryCalled);
        Assert.Equal("value", cached?.Value);
    }

    [Fact]
    public async Task Create_WithProductionApiAndMemoryCache_CacheManagerStoresAndReads()
    {
        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30));
        });

        var cacheManager = GetCacheManager(client);
        Assert.NotNull(cacheManager);

        var factoryCalled = false;
        var cached = await cacheManager.GetOrSetAsync("production-test", _ =>
        {
            factoryCalled = true;
            return Task.FromResult<CacheEntry<string>?>(
                new CacheEntry<string>("value", ["item_production"]));
        });

        Assert.True(factoryCalled);
        Assert.Equal("value", cached?.Value);
    }

    [Fact]
    public async Task Create_WithHybridCache_CreatesClient()
    {
        var distributedCache = new TestDistributedCache();

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.AddSingleton<IDistributedCache>(distributedCache);
            d.UseHybridCache(opts => opts.DefaultExpiration = TimeSpan.FromHours(1));
        });

        Assert.NotNull(client);
    }

    [Fact]
    public void Create_WithHybridCacheButNoDistributedCache_ThrowsInvalidOperationException()
    {
        // The hybrid cache reads IDistributedCache from the container; nothing registers one on its behalf.
        Assert.Throws<InvalidOperationException>(() => DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.UseHybridCache();
        }));
    }

    [Fact]
    public async Task Create_WithAllOptions_CreatesClient()
    {
        var typeProvider = new TestTypeProvider();

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o =>
            {
                o.EnvironmentId = EnvironmentId;
                o.DefaultRenditionPreset = "mobile";
            });
            d.Services.AddSingleton<ITypeProvider>(typeProvider);
            d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(15));
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task Create_CallingMemoryCacheAfterHybridCache_UsesLastConfigured()
    {
        var distributedCache = new TestDistributedCache();

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.AddSingleton<IDistributedCache>(distributedCache);
            d.UseHybridCache();
            d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30));
        });

        Assert.NotNull(client);
        Assert.IsType<MemoryCacheManager>(GetCacheManager(client));
    }

    [Fact]
    public async Task Create_CallingHybridCacheAfterMemoryCache_UsesLastConfigured()
    {
        var distributedCache = new TestDistributedCache();

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.AddSingleton<IDistributedCache>(distributedCache);
            d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30));
            d.UseHybridCache(opts => opts.DefaultExpiration = TimeSpan.FromHours(1));
        });

        Assert.NotNull(client);
        Assert.IsType<HybridCacheManager>(GetCacheManager(client));
    }

    [Fact]
    public async Task Create_FluentCalls_ReturnTheSameBuilder()
    {
        IDeliveryClientBuilder? seen = null;
        IDeliveryClientBuilder? afterResilience = null;
        IDeliveryClientBuilder? afterCache = null;

        await using var client = DeliveryClient.Create(d =>
        {
            seen = d;
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            afterResilience = d.ConfigureResilience(_ => { });
            afterCache = d.UseMemoryCache();
        });

        Assert.Same(seen, afterResilience);
        Assert.Same(seen, afterCache);
    }

    [Fact]
    public async Task Create_MultipleClients_AreIndependent()
    {
        await using var client1 = DeliveryClient.Create(d => d.Options.Configure(o => o.EnvironmentId = EnvironmentId));

        await using var client2 = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.UseMemoryCache();
        });

        Assert.NotNull(client1);
        Assert.NotNull(client2);
        Assert.NotSame(client1, client2);
    }

    [Fact]
    public async Task Create_WithMemoryCacheAdvanced_CreatesClient()
    {
        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.UseMemoryCache(opts =>
            {
                opts.DefaultExpiration = TimeSpan.FromMinutes(15);
                opts.IsFailSafeEnabled = true;
            });
        });

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IDeliveryClient>(client);
    }

    [Fact]
    public async Task Create_WithHybridCacheAdvanced_CreatesClient()
    {
        var distributedCache = new TestDistributedCache();

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.AddSingleton<IDistributedCache>(distributedCache);
            d.UseHybridCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30));
        });

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IDeliveryClient>(client);
    }

    private sealed class BuilderSiblingOptions
    {
        public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
    }

    [Fact]
    public async Task Create_WithMemoryCache_ServiceProviderCallback_InvokesCallbackOnResolution()
    {
        var invokedWithExpiration = TimeSpan.Zero;

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.Configure<BuilderSiblingOptions>(o => o.CacheExpiration = TimeSpan.FromHours(3));
            d.UseMemoryCache((sp, opts) =>
            {
                opts.DefaultExpiration = sp.GetRequiredService<IOptions<BuilderSiblingOptions>>().Value.CacheExpiration;
                invokedWithExpiration = opts.DefaultExpiration;
            });
        });

        Assert.NotNull(client);
        Assert.NotNull(GetCacheManager(client));
        Assert.Equal(TimeSpan.FromHours(3), invokedWithExpiration);
    }

    [Fact]
    public async Task Create_WithHybridCache_ServiceProviderCallback_InvokesCallbackOnResolution()
    {
        var distributedCache = new TestDistributedCache();
        var invokedWithExpiration = TimeSpan.Zero;

        await using var client = DeliveryClient.Create(d =>
        {
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            d.Services.AddSingleton<IDistributedCache>(distributedCache);
            d.Services.Configure<BuilderSiblingOptions>(o => o.CacheExpiration = TimeSpan.FromHours(2));
            d.UseHybridCache((sp, opts) =>
            {
                opts.DefaultExpiration = sp.GetRequiredService<IOptions<BuilderSiblingOptions>>().Value.CacheExpiration;
                invokedWithExpiration = opts.DefaultExpiration;
            });
        });

        Assert.NotNull(client);
        Assert.NotNull(GetCacheManager(client));
        Assert.Equal(TimeSpan.FromHours(2), invokedWithExpiration);
    }

    [Fact]
    public void UseMemoryCache_ServiceProviderCallback_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DeliveryClient.Create(d =>
            d.UseMemoryCache((Action<IServiceProvider, DeliveryCacheOptions>)null!)));
    }

    [Fact]
    public void UseHybridCache_ServiceProviderCallback_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DeliveryClient.Create(d =>
            d.UseHybridCache((Action<IServiceProvider, DeliveryCacheOptions>)null!)));
    }

    [Fact]
    public async Task Builder_ExposesTheNameServicesOptionsAndHttpClient()
    {
        await using var client = DeliveryClient.Create(d =>
        {
            Assert.Equal("Default", d.Name);
            Assert.NotNull(d.Services);
            Assert.Equal("Default", d.Options.Name);
            Assert.Equal("Kontent.Ai.Delivery.HttpClient.Default", d.HttpClient.Name);
            d.Options.Configure(o => o.EnvironmentId = EnvironmentId);
        });
    }

    // The type the caller has to catch. It comes out of the options pipeline, not from the builder, so it
    // is not an InvalidOperationException.
    [Fact]
    public void Create_InvalidOptions_ThrowsOptionsValidationException()
    {
        var act = () => DeliveryClient.Create(d => d.Options.Configure(o => o.EnvironmentId = "not-a-guid"));

        var exception = Assert.Throws<OptionsValidationException>(act);
        Assert.Contains("EnvironmentId", exception.Message);
    }

    // Simple test implementation of ITypeProvider
    private class TestTypeProvider : ITypeProvider
    {
        public Type? GetType(string contentType) => null;
        public string? GetCodename(Type contentType) => null;
    }

    // Create hands back the DeliveryClient itself, so the cache manager it captured is one field away -
    // no wrapper to unwrap.
    private static IDeliveryCacheManager? GetCacheManager(IDeliveryClient client)
    {
        var cacheManagerField = client.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(f => typeof(IDeliveryCacheManager).IsAssignableFrom(f.FieldType));

        return cacheManagerField?.GetValue(client) as IDeliveryCacheManager;
    }

    // Simple test implementation of IDistributedCache
    private class TestDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _cache = [];

        public byte[]? Get(string key) =>
            _cache.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _cache[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.CompletedTask;

        public void Remove(string key) =>
            _cache.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
