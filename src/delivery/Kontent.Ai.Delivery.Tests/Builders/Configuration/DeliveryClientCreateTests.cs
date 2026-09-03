using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.Builders.Configuration;

public sealed class DeliveryClientCreateTests : IDisposable
{
    private const string EnvironmentId = "550cec62-90a6-4ab3-b3e4-3d0bb4c04f5c";
    private const string TestPreviewApiKey = "preview.api.key";
    private const string TestSecureApiKey = "secure.api.key";
    private const string ProductionItemsUrl = $"https://deliver.kontent.ai/{EnvironmentId}/items";
    private const string PreviewItemsUrl = $"https://preview-deliver.kontent.ai/{EnvironmentId}/items";

    private static readonly string ItemsJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Fixtures", "DeliveryClient", "items.json"));

    private readonly MockHttpMessageHandler _http = new();

    public void Dispose() => _http.Dispose();

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
    public async Task Create_ProductionApi_SendsToTheProductionEndpointWithoutAKey()
    {
        var captured = ExpectItems(ProductionItemsUrl);

        await using var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Null(captured.Request!.Headers.Authorization);
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_PreviewApi_SendsToThePreviewEndpointWithTheKey()
    {
        var captured = ExpectItems(PreviewItemsUrl);

        await using var client = CreateClient(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UsePreviewApi(TestPreviewApiKey);
        });

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Equal(TestPreviewApiKey, captured.Request!.Headers.Authorization?.Parameter);
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_SecureAccess_SendsToTheProductionEndpointWithTheKey()
    {
        var captured = ExpectItems(ProductionItemsUrl);

        await using var client = CreateClient(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UseProductionApi(TestSecureApiKey);
        });

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Equal(TestSecureApiKey, captured.Request!.Headers.Authorization?.Parameter);
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_FromOptionsInstance_CopiesTheValues()
    {
        var captured = ExpectItems(PreviewItemsUrl);
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId }.UsePreviewApi(TestPreviewApiKey);

        await using var client = DeliveryClient.Create(options, d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Equal(TestPreviewApiKey, captured.Request!.Headers.Authorization?.Parameter);
        _http.VerifyNoOutstandingExpectation();
    }

    // The copy contract: the instance is copied, not held, so a change to it after Create stays with the caller.
    [Fact]
    public async Task Create_FromOptionsInstance_MutatingItAfterwardsDoesNotReachTheClient()
    {
        var captured = ExpectItems(ProductionItemsUrl);
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId };
        await using var client = DeliveryClient.Create(options, d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

        options.UsePreviewApi(TestPreviewApiKey);

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Null(captured.Request!.Headers.Authorization);
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_WithMemoryCache_ExposesTheCacheManager()
    {
        // A standalone client owns its container, so the client is the only way to reach the cache
        // inside it - and a webhook handler has to, to invalidate.
        _http.When(HttpMethod.Get, $"https://deliver.kontent.ai/{EnvironmentId}/types")
            .Respond("application/json", """{"types":[],"pagination":{"skip":0,"limit":0,"count":0,"next_page":""}}""");
        var attempts = new AttemptCounter();

        await using var client = CreateClient(
            o =>
            {
                o.EnvironmentId = EnvironmentId;
                o.EnableResilience = false;
            },
            d =>
            {
                d.HttpClient.AddHttpMessageHandler(() => attempts);
                d.UseMemoryCache();
            });

        await client.GetTypes().ExecuteAsync();
        await client.GetTypes().ExecuteAsync();
        Assert.Equal(1, attempts.Count);

        Assert.NotNull(client.CacheManager);
        await client.CacheManager.InvalidateAsync([DeliveryCacheDependencies.TypesListScope]);
        await client.GetTypes().ExecuteAsync();

        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task Create_WithoutACache_HasNoCacheManager()
    {
        await using var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        Assert.Null(client.CacheManager);
    }

    [Fact]
    public async Task Create_WithDisabledResilience_DoesNotRetry()
    {
        _http.When(HttpMethod.Get, ProductionItemsUrl).Respond(HttpStatusCode.InternalServerError);
        var attempts = new AttemptCounter();

        await using var client = CreateClient(
            o =>
            {
                o.EnvironmentId = EnvironmentId;
                o.EnableResilience = false;
            },
            d => d.HttpClient.AddHttpMessageHandler(() => attempts));

        var result = await client.GetItems().ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(1, attempts.Count);
    }

    [Fact]
    public async Task Create_WithTypeProvider_ConsultsItForEveryContentType()
    {
        ExpectItems(ProductionItemsUrl);
        var typeProvider = new RecordingTypeProvider();

        await using var client = CreateClient(
            o => o.EnvironmentId = EnvironmentId,
            d => d.Services.AddSingleton<ITypeProvider>(typeProvider));

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Contains("article", typeProvider.Requested);
    }

    [Fact]
    public async Task Create_WithLoggerFactory_LogsThroughIt()
    {
        ExpectItems(ProductionItemsUrl);
        var entries = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(entries));

        await using var client = CreateClient(
            o => o.EnvironmentId = EnvironmentId,
            d => d.Services.AddSingleton(loggerFactory));

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Contains(entries.Categories, category => category.StartsWith("Kontent.Ai.Delivery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_WithMemoryCache_ServesARepeatedQueryFromTheCache()
    {
        // One expectation: the second query can only succeed if the cache serves it.
        ExpectItems(ProductionItemsUrl);

        await using var client = CreateClient(
            o => o.EnvironmentId = EnvironmentId,
            d => d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(30)));

        Assert.IsType<MemoryCacheManager>(GetCacheManager(client));
        Assert.Equal(ResponseSource.Origin, (await client.GetItems<object>().ExecuteAsync()).ResponseSource);
        Assert.Equal(ResponseSource.Cache, (await client.GetItems<object>().ExecuteAsync()).ResponseSource);
        _http.VerifyNoOutstandingExpectation();
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
    public async Task Create_WithHybridCache_ServesARepeatedQueryFromTheCache()
    {
        ExpectItems(ProductionItemsUrl);
        var distributedCache = new TestDistributedCache();

        await using var client = CreateClient(
            o => o.EnvironmentId = EnvironmentId,
            d =>
            {
                d.Services.AddSingleton<IDistributedCache>(distributedCache);
                d.UseHybridCache(opts => opts.DefaultExpiration = TimeSpan.FromHours(1));
            });

        Assert.IsType<HybridCacheManager>(GetCacheManager(client));
        Assert.Equal(ResponseSource.Origin, (await client.GetItems<object>().ExecuteAsync()).ResponseSource);
        Assert.Equal(ResponseSource.Cache, (await client.GetItems<object>().ExecuteAsync()).ResponseSource);
        _http.VerifyNoOutstandingExpectation();
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
    public async Task Create_WithAllOptions_AppliesEachOfThem()
    {
        ExpectItems(ProductionItemsUrl);
        var typeProvider = new RecordingTypeProvider();

        await using var client = CreateClient(
            o =>
            {
                o.EnvironmentId = EnvironmentId;
                o.DefaultRenditionPreset = "mobile";
            },
            d =>
            {
                d.Services.AddSingleton<ITypeProvider>(typeProvider);
                d.UseMemoryCache(opts => opts.DefaultExpiration = TimeSpan.FromMinutes(15));
            });

        Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        Assert.Contains("article", typeProvider.Requested);
        Assert.IsType<MemoryCacheManager>(GetCacheManager(client));
        Assert.Equal("mobile", OwnedServices(client).GetRequiredService<IOptionsMonitor<DeliveryOptions>>().Get("Default").DefaultRenditionPreset);
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

    // Each client owns its own container, so disposing one must leave the other usable.
    [Fact]
    public async Task Create_MultipleClients_AreIndependent()
    {
        _http.When(HttpMethod.Get, ProductionItemsUrl).Respond("application/json", ItemsJson);
        var client1 = CreateClient(o => o.EnvironmentId = EnvironmentId);
        await using var client2 = CreateClient(o => o.EnvironmentId = EnvironmentId, d => d.UseMemoryCache());

        Assert.NotSame(client1, client2);
        await client1.DisposeAsync();

        Assert.True((await client2.GetItems().ExecuteAsync()).IsSuccess);
    }

    // The point of Create handing the client its container: disposing the client tears the transport down.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisposingTheClient_FailsEveryFurtherRequest(bool enableResilience)
    {
        _http.When(HttpMethod.Get, ProductionItemsUrl).Respond("application/json", ItemsJson);
        DeliveryClient client;
        await using (client = CreateClient(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.EnableResilience = enableResilience;
        }))
        {
            Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        }

        var afterDispose = await client.GetItems().ExecuteAsync();

        Assert.False(afterDispose.IsSuccess);
        Assert.True(HasObjectDisposed(afterDispose.Error?.Exception), afterDispose.Error?.Exception?.ToString());
    }

    [Fact]
    public async Task DisposingTheClientSynchronously_FailsEveryFurtherRequest()
    {
        _http.When(HttpMethod.Get, ProductionItemsUrl).Respond("application/json", ItemsJson);
        DeliveryClient client;
        using (client = CreateClient(o => o.EnvironmentId = EnvironmentId))
        {
            Assert.True((await client.GetItems().ExecuteAsync()).IsSuccess);
        }

        var afterDispose = await client.GetItems().ExecuteAsync();

        Assert.False(afterDispose.IsSuccess);
        Assert.True(HasObjectDisposed(afterDispose.Error?.Exception), afterDispose.Error?.Exception?.ToString());
    }

    // Create builds a container and only then finds the options invalid; the container must not leak.
    [Fact]
    public void Create_WhenConstructionFails_DisposesThePrivateContainer()
    {
        DisposableProbe? probe = null;

        var act = () => DeliveryClient.Create(d =>
        {
            d.Services.AddSingleton(_ => probe = new DisposableProbe());
            d.Options.Configure<DisposableProbe>((o, _) => o.EnvironmentId = "not-a-guid");
        });

        Assert.Throws<OptionsValidationException>(act);
        Assert.NotNull(probe);
        Assert.True(probe.Disposed);
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

    private DeliveryClient CreateClient(Action<DeliveryOptions> configureOptions, Action<IDeliveryClientBuilder>? configure = null)
        => DeliveryClient.Create(d =>
        {
            d.Options.Configure(configureOptions);
            d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http);
            configure?.Invoke(d);
        });

    private CapturedRequest ExpectItems(string url)
    {
        var captured = new CapturedRequest();
        _http.Expect(HttpMethod.Get, url).Respond(request =>
        {
            captured.Request = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ItemsJson, Encoding.UTF8, "application/json"),
            };
        });
        return captured;
    }

    private static bool HasObjectDisposed(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ObjectDisposedException)
            {
                return true;
            }
        }

        return false;
    }

    // Create hands the client its own container as the resource it owns, one field away.
    private static IServiceProvider OwnedServices(DeliveryClient client)
    {
        var field = typeof(DeliveryClient).GetField("_ownedResources", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsAssignableFrom<IServiceProvider>(field!.GetValue(client));
    }

    private sealed class CapturedRequest
    {
        public HttpRequestMessage? Request { get; set; }
    }

    private sealed class AttemptCounter : DelegatingHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class RecordingTypeProvider : ITypeProvider
    {
        public List<string> Requested { get; } = [];

        public Type? GetType(string contentType)
        {
            Requested.Add(contentType);
            return null;
        }

        public string? GetCodename(Type contentType) => null;
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Categories { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, Categories);

        public void Dispose() { }

        private sealed class CollectingLogger(string category, ConcurrentBag<string> categories) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => categories.Add(category);
        }
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
