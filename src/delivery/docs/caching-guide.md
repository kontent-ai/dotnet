# Caching Guide

Caching is essential for production applications using the Kontent.ai Delivery API. This guide covers all aspects of implementing effective caching strategies, from basic memory caching to sophisticated webhook-based invalidation.

## Table of Contents

- [Why Caching Matters](#why-caching-matters)
- [Cache Types](#cache-types)
  - [Memory Cache](#memory-cache)
  - [Hybrid Cache](#hybrid-cache)
- [Configuration](#configuration)
  - [Memory Cache Setup](#memory-cache-setup)
  - [Hybrid Cache Setup](#hybrid-cache-setup)
  - [Configuring Cache Options from DI Services](#configuring-cache-options-from-di-services)
  - [Custom Cache Manager](#custom-cache-manager)
- [How Caching Works](#how-caching-works)
  - [Cache Keys](#cache-keys)
  - [Dependency Tracking](#dependency-tracking)
  - [Using Dependency Keys for Output Caching](#using-dependency-keys-for-output-caching)
  - [Expiration Strategies](#expiration-strategies)
    - [Per-query Expiration Override](#per-query-expiration-override)
- [Cache Invalidation](#cache-invalidation)
  - [Invalidation Matrix (RC-ready)](#invalidation-matrix-rc-ready)
  - [Manual Invalidation](#manual-invalidation)
  - [Webhook-Based Invalidation](#webhook-based-invalidation)
  - [Timed Invalidation](#timed-invalidation)
- [Per-Client Caching](#per-client-caching)
  - [Enabling Caching for Named Clients](#enabling-caching-for-named-clients)
  - [Cache Key Prefixing](#cache-key-prefixing)
  - [Hybrid Cache for Named Clients](#hybrid-cache-for-named-clients)
- [Multi-Tenant Caching](#multi-tenant-caching)
  - [Complete Multi-Tenant Example](#complete-multi-tenant-example)
  - [Per-Tenant Cache Invalidation](#per-tenant-cache-invalidation)
  - [Selective Caching (Production vs Preview)](#selective-caching-production-vs-preview)
- [Best Practices](#best-practices)
- [Monitoring and Diagnostics](#monitoring-and-diagnostics)
  - [Optional Redis Validation Suite](#optional-redis-validation-suite)
- [Troubleshooting](#troubleshooting)

## Why Caching Matters

### API Rate Limits

Kontent.ai enforces rate limits on API requests:
- Without caching, you can quickly hit these limits
- Repeated requests for the same content waste quota
- Caching dramatically reduces API calls

### Performance

- **Reduced Latency**: Cached responses are served in microseconds vs. milliseconds for API calls
- **Lower Bandwidth**: No network round-trip for cached content
- **Better User Experience**: Faster page loads and responses

### Cost Optimization

- Fewer API calls mean lower costs in high-traffic scenarios
- Reduced infrastructure requirements for handling API responses
- Better resource utilization

## Cache Types

### Memory Cache

**Pros:**
- Fastest possible cache access (microseconds)
- No external dependencies
- Simple setup

**Cons:**
- Limited to single server (not shared across instances)
- Memory pressure on large datasets
- Lost on application restart

**When to Use:**
- Single-server deployments
- Development and testing
- Low to moderate traffic applications

**What a hit hands back:** the memory cache stores the hydrated objects themselves, so every hit returns the same instances the previous caller received, with the client's `DefaultRenditionPreset` and `CustomAssetDomain` already applied. Treat cached models as read-only - a mutation is visible to every later caller - and purge after changing either option at runtime, since cached entries keep the values they were built with.

### Hybrid Cache

**Pros:**
- Shared across multiple application instances
- Survives application restarts
- Scalable to large datasets
- Can be managed independently

**Note:** The SDK stores raw JSON payloads in hybrid caches and rehydrates on read. This avoids circular reference serialization issues and keeps payloads portable across instances.

Built-in hybrid cache invalidation uses dependency tags through [FusionCache](https://github.com/ZiggyCreatures/FusionCache). Register a FusionCache backplane and the SDK picks it up from the container, propagating invalidations to the other nodes:

```csharp
services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
services.AddFusionCacheStackExchangeRedisBackplane(o => o.Configuration = "localhost:6379");
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.UseHybridCache();
});
```

Without one, part of the invalidation state stays local to each instance, so whether a node observes another's `InvalidateAsync` depends on the order the two read and invalidated in — a node can go on serving content that was already evicted, until the entry expires by itself. A single-instance application needs no backplane.

> [!NOTE]
> **Every hybrid hit rehydrates.** The SDK stores the raw payload in both tiers, so that a value can move between them unchanged; a hit therefore parses the JSON and maps the elements again, rich text HTML parse included. That is measurable on a hot path serving large rich-text items, and it is also what makes every hit a fresh instance. If maximum read throughput matters more than sharing the cache across instances, use `UseMemoryCache`.

Two costs worth knowing about the distributed tier. Every dependency key is a tag, and FusionCache verifies an entry's tags on each read against tag data it keeps in memory - a node that has never seen a tag reads it from the distributed cache once, so the first hit on a fresh node costs one round trip per tag (a listing of a hundred items with their types, assets and taxonomies carries several hundred). And an invalidation is remembered for as long as that tag data lives: ten days by default, adjustable through `ConfigureFusionCache(f => f.TagsDefaultEntryOptions.Duration = …)`, which must stay above your longest expiration, per-query overrides included.

If the distributed cache is unreachable, the SDK works around it: the memory tier or the origin answers, a two-second circuit breaker keeps a dead Redis from being retried on every request, and FusionCache re-syncs the tier when it is back. Nothing is thrown out of a query for it; enable the `ZiggyCreatures.Caching.Fusion.FusionCache` log category to see it happen.

**Cons:**
- Network latency (still faster than API calls)
- Requires external infrastructure (Redis, SQL Server, etc.)
- Additional configuration complexity

**When to Use:**
- Production environments with multiple servers
- High-availability requirements
- Large-scale applications
- Cloud deployments

## Configuration

Caching is provided by the standalone `Kontent.Ai.Delivery.Caching` package:

```bash
dotnet add package Kontent.Ai.Delivery.Caching
```

This package adds `UseMemoryCache`, `UseHybridCache` and `UseCacheManager` to `IDeliveryClientBuilder`, so a client's cache is configured in the same `AddDeliveryClient` (or `DeliveryClient.Create`) callback as its options. All are [FusionCache](https://github.com/ZiggyCreatures/FusionCache)-backed while keeping the same public `IDeliveryCacheManager` contract.

### Memory Cache Setup

#### Basic Configuration

```csharp
using Kontent.Ai.Delivery;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Single client scenario - no name required
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});

var serviceProvider = services.BuildServiceProvider();
```

#### Named Clients

For multi-client scenarios, use named clients:

```csharp
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromMinutes(30));
});
```

#### Advanced Memory Cache Configuration

The SDK uses the application's `IMemoryCache`, so its options apply. Under a `SizeLimit` every entry the SDK writes counts as one unit, so the limit bounds the number of cached responses rather than their bytes:

```csharp
services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024;  // At most 1024 cached responses
    options.CompactionPercentage = 0.25;  // Remove 25% when limit hit
});

services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});
```

### Hybrid Cache Setup

#### Redis Cache

```csharp
using StackExchange.Redis;

// Register Redis distributed cache
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "KontentCache_";
});

// Single client scenario
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(2));
});

// Or with named clients for multi-client scenarios:
// services.AddDeliveryClient("production", delivery =>
// {
//     delivery.Options.Configure(options => { ... });
//     delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(2));
// });
```

#### Redis with Connection Multiplexer

```csharp
// Register Redis connection
services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse("localhost:6379");
    configuration.AbortOnConnectFail = false;
    configuration.ConnectTimeout = 5000;
    return ConnectionMultiplexer.Connect(configuration);
});

services.AddStackExchangeRedisCache(options =>
{
    options.ConnectionMultiplexerFactory = async () =>
        await Task.FromResult(serviceProvider.GetRequiredService<IConnectionMultiplexer>());
    options.InstanceName = "Kontent_";
});

services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(4));
});
```

#### SQL Server Distributed Cache

```csharp
services.AddDistributedSqlServerCache(options =>
{
    options.ConnectionString = configuration.GetConnectionString("CacheDb");
    options.SchemaName = "dbo";
    options.TableName = "KontentCache";
});

services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});
```

#### Azure Cache for Redis

```csharp
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = configuration.GetConnectionString("AzureRedis");
    options.InstanceName = "Production_Kontent_";
});

services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(6));
});
```

### Configuring Cache Options from DI Services

Use the `IServiceProvider` cache overloads when cache settings need to be composed from other registered services, such as application options bound from configuration:

```csharp
services.Configure<SiteOptions>(configuration.GetSection("Site"));

services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure<IServiceProvider>((options, sp) =>
    {
        var site = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
        options.EnvironmentId = site.EnvironmentId;
    });
    delivery.UseMemoryCache((sp, options) =>
    {
        var site = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
        options.DefaultExpiration = site.CacheExpiration;
        options.IsFailSafeEnabled = true;
    });
});
```

Timing matters: the plain `Action<DeliveryCacheOptions>` overloads run immediately during service registration and validate cache options immediately. The `(IServiceProvider, DeliveryCacheOptions)` overloads run later, when the keyed singleton `IDeliveryCacheManager` is first resolved from the root provider, so validation is deferred to that first resolution.

#### Reaching into FusionCache

`ConfigureFusionCache` hands you the `FusionCacheOptions` the SDK built, after its defaults are applied. Every SDK operation starts from the `DefaultEntryOptions` you leave there, so a `Size`, a distributed-cache timeout or the background-operation flags take effect; `TagsDefaultEntryOptions` is what an invalidation is stored with. What the SDK decides stays decided: the duration, fail-safe, jitter and eager-refresh policy come from `DeliveryCacheOptions`, serialization failures are thrown, distributed-cache and backplane failures are not.

```csharp
delivery.UseHybridCache(cache => cache.ConfigureFusionCache(fusion =>
{
    fusion.DefaultEntryOptions.AllowBackgroundBackplaneOperations = true;
    fusion.TagsDefaultEntryOptions.Duration = TimeSpan.FromDays(30);
}));
```

Because the cache manager is a singleton, resolve only singleton-safe dependencies from cache callbacks, such as `IOptions<T>`, `IOptionsMonitor<T>`, configuration, or loggers. Do not depend on scoped/request services such as `IOptionsSnapshot<T>`, `DbContext`, tenant request context, or per-request `HttpContext` state.

### Custom Cache Manager

For advanced scenarios, implement a custom cache manager. The `IDeliveryCacheManager` interface uses a factory-based `GetOrSetAsync` pattern — the factory is invoked on cache miss and returns a `CacheEntry<T>?`. A `null` means the origin has no value for the key: don't cache, and drop any stale copy you keep for fail-safe. A thrown exception means the origin could not be reached: serve a stale copy if you keep one, otherwise let it propagate. The method returns `CacheResult<T>?` (a record containing the `Value` and the collected `DependencyKeys`) so that downstream consumers can access dependency metadata.

Use the default `StorageMode` (`CacheStorageMode.HydratedObject`) for hydrated-object caching (memory), or override `StorageMode` to `CacheStorageMode.RawJson` for raw JSON payload caching (distributed). A manager that serves stale copies while the origin is unreachable sets `IsStale` on the results it serves that way; the SDK reports them as `ResponseSource.FailSafe`.

#### Hydrated-object cache manager (memory style)

```csharp
using System.Collections.Concurrent;
using Kontent.Ai.Delivery.Abstractions;

public class CustomMemoryCacheManager : IDeliveryCacheManager
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public async Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (_cache.TryGetValue(cacheKey, out var cached))
            return new CacheResult<T>((T)cached, []);

        var entry = await factory(cancellationToken);
        if (entry is null) return null;

        _cache.TryAdd(cacheKey, entry.Value);
        return new CacheResult<T>(entry.Value, entry.Dependencies.ToArray());
    }

    public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
    {
        // Implement dependency tracking + invalidation for production use
        return Task.FromResult(true);
    }
}
```

#### Raw JSON cache manager (hybrid style)

```csharp
using System.Text.Json;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

public class CustomHybridCacheManager : IDeliveryCacheManager
{
    private readonly IDistributedCache _cache;

    public CustomHybridCacheManager(IDistributedCache cache) => _cache = cache;

    // Tell the SDK to cache raw JSON payloads instead of hydrated objects
    public CacheStorageMode StorageMode => CacheStorageMode.RawJson;

    public async Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var json = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (json is not null)
            return new CacheResult<T>(JsonSerializer.Deserialize<T>(json)!, []);

        var entry = await factory(cancellationToken);
        if (entry is null) return null;

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(1)
        };

        var serialized = JsonSerializer.Serialize(entry.Value);
        await _cache.SetStringAsync(cacheKey, serialized, options, cancellationToken);

        // Implement dependency index + invalidation for production use
        return new CacheResult<T>(entry.Value, entry.Dependencies.ToArray());
    }

    public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

// Registration
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseCacheManager(sp => new CustomHybridCacheManager(sp.GetRequiredService<IDistributedCache>()));
});
```

## How Caching Works

### Cache Coverage

SDK caching applies to cacheable query builders (for example, strongly-typed item/list queries and type/taxonomy queries).

Dynamic item/list queries (`GetItem()` and `GetItems()` without a generic model type) are intentionally non-cacheable because their final item types are resolved at runtime. These queries always execute against the API and return `IsCacheHit == false`.

When `WaitForLoadingNewContent(true)` is enabled for a query, the SDK bypasses local caching for that request path (no cache lookup and no cache store).

When a client is configured with `UsePreviewApi = true`, the SDK always bypasses local cache reads/writes for that client, even if a cache manager is registered.

A typed query whose model is `IDynamicElements` or `DynamicElements` is cached, but its elements are not mapped, so only item, type and list-scope dependencies are tracked for it - not the assets, taxonomy groups and rich-text links a mapped model would add.

### Cache Keys

Cache keys are automatically generated from query parameters using a deterministic, human-readable format.

#### Key Format

The general format is: `{queryType}:{identifier}:{params}:{filters}`

| Query Type | Format | Example |
|------------|--------|---------|
| Single Item | `item:{codename}:lang={lang}:depth={n}:elements={sorted}` | `item:homepage:lang=en-US:depth=2` |
| List Items | `items:lang={lang}:depth={n}:skip={n}:limit={n}:filters={hash}` | `items:lang=en-US:skip=0:limit=10` |
| Single Type | `type:{codename}:elements={sorted}` | `type:article:elements=name\|codename` |
| List Types | `types:skip={n}:limit={n}:elements={sorted}:filters={hash}` | `types:skip=0:limit=25` |
| Single Taxonomy | `taxonomy:{codename}` | `taxonomy:categories` |
| List Taxonomies | `taxonomies:skip={n}:limit={n}:filters={hash}` | `taxonomies:skip=0:limit=100` |

#### Key Properties

- **Deterministic**: Same parameters always produce the same key
- **Order-independent**: Arrays and filter dictionaries in different orders produce the same key
- **Human-readable**: Common parameters are visible for debugging (e.g., `lang=en-US:depth=2`)
- **Efficient**: Filters are hashed to keep keys compact when queries are complex

#### Examples

```csharp
// Single item query
await client.GetItem<Article>("homepage").ExecuteAsync();
// Key: item:homepage

// Item with language and depth
await client.GetItem<Article>("homepage")
    .WithLanguage("de-DE")
    .Depth(2)
    .ExecuteAsync();
// Key: item:homepage:lang=de-DE:depth=2

// Item with element projection
await client.GetItem<Article>("homepage")
    .WithElements("title", "description")
    .ExecuteAsync();
// Key: item:homepage:elements=description|title  (sorted alphabetically)

// Items listing with pagination
await client.GetItems<Article>()
    .Skip(10)
    .Limit(5)
    .ExecuteAsync();
// Key: items:skip=10:limit=5

// Items with filters (filters are hashed for brevity)
await client.GetItems<Article>()
    .Where(f => f.System("type").IsEqualTo("article"))
    .Where(f => f.Element("category").IsIn("news", "blog"))
    .ExecuteAsync();
// Key: items:filters=A7F3E2B9C1D5  (12-char hash of sorted filter parameters)

// Taxonomy query
await client.GetTaxonomy("categories").ExecuteAsync();
// Key: taxonomy:categories
```

#### Filter Hashing

When queries include filters, they are hashed using SHA256 (first 12 characters of URL-safe base64):

- Filters are sorted by key, then by value before hashing
- This ensures `{("a", "1"), ("b", "2")}` and `{("b", "2"), ("a", "1")}` produce the same hash
- The 12-character hash provides ~72 bits of entropy (extremely low collision probability)

#### Key Prefixing

Every key a client stores lives under `{KeyPrefix}:{EnvironmentId}:`, where `KeyPrefix` defaults to the client's name for a named client and to nothing for the default one. The environment id is always there, because a store can outlive the process and be shared: two applications on different environments sharing one Redis would otherwise compute the same key for "the item `homepage`". Hybrid keys carry a format version after that, `v1:`, so an SDK release that changes what it stores misses on old entries instead of failing to read them.

**Default (single-client) scenario:**
```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(o => o.EnvironmentId = "...");
    delivery.UseMemoryCache();
});
// Keys: {environmentId}:item:homepage, {environmentId}:items:skip=0:limit=10, etc.
```

**Named clients (multi-client scenario):**
```csharp
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(o => o.EnvironmentId = "...");
    delivery.UseMemoryCache();
});
// Keys: production:{environmentId}:item:homepage, etc.
```

**Custom prefix:**
```csharp
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(o => o.EnvironmentId = "...");
    delivery.UseMemoryCache(o => o.KeyPrefix = "prod");
});
// Keys: prod:{environmentId}:item:homepage, etc.
// In a hybrid cache: prod:{environmentId}:v1:item:homepage
```

An explicit `KeyPrefix = ""` on a named client puts it in the same namespace as the default client on that environment - the two then share entries, invalidations and purges, which is what you want only if they are the same client registered twice.

The prefix is handed to FusionCache, so it covers FusionCache's own bookkeeping too: the tag data an invalidation writes and the marker a purge writes. Two clients sharing one memory cache or one Redis cannot reach each other's entries, and purging one leaves the other's alone.

`DefaultRenditionPreset` and `CustomAssetDomain` are not part of the keys; use separate named clients per configuration. The environment id is read when the cache is created, so changing it at runtime on an existing cached client keeps caching under the old namespace - purge, or recreate the client.

### Dependency Tracking

The SDK automatically tracks content dependencies for every query. These dependency keys serve two purposes: they drive internal cache tag-based invalidation, and they are surfaced on the delivery result itself (via `IDeliveryResult<T>.DependencyKeys`) so that your application can use them for downstream caching scenarios such as ASP.NET output-cache tagging.

```csharp
// When you retrieve an article with linked authors
var result = await client.GetItem<Article>("my-article")
    .Depth(2)
    .ExecuteAsync();

// The following dependencies are tracked:
// - item_my-article
// - item_author1 (if linked)
// - item_author2 (if linked)
// - type_article (+ type_{codename} for each linked item's content type)
// - Any assets used in the content
```

This enables targeted cache invalidation when specific content changes.

Cached `GetItems<T>()` queries also include a synthetic scope dependency:
- `DeliveryCacheDependencies.ItemsListScope` (`scope_items_list`)

Use this key when an item event may change which cached lists an item belongs to (for example, new item publish or metadata update). Invalidating the scope key clears all cached typed item-list queries in the current cache namespace.

Cached `GetTypes()` and `GetTaxonomies()` queries use the same pattern:
- `DeliveryCacheDependencies.TypesListScope` (`scope_types_list`) for type listings
- `DeliveryCacheDependencies.TaxonomiesListScope` (`scope_taxonomies_list`) for taxonomy listings

Single type queries use direct keys in the format `type_{codename}` (for example, `type_article`). The same `type_{codename}` key is **also** attached to every cached item / item-list query whose payload references at least one item of that type (including linked items, modular content, and rich-text inline items). Invalidating `type_article` therefore evicts both the cached type definition and any item caches referencing articles — the recommended signal for content-type-change webhooks.

### Using Dependency Keys for Output Caching

The SDK exposes dependency keys on every delivery result via the `DependencyKeys` property on `IDeliveryResult<T>`. This enables downstream cache invalidation scenarios such as ASP.NET output-cache tagging — you can tag your controller-level or page-level cache entries with the same keys the SDK uses internally.

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess && result.DependencyKeys is { } keys)
{
    // Tag your output cache entry with the SDK's dependency keys
    foreach (var key in keys)
    {
        HttpContext.Response.Headers.Append("Cache-Tag", key);
    }
}
```

Dependency keys are always available — they are collected regardless of whether SDK caching is configured. The key formats are the same canonical formats used for SDK cache invalidation (see [Invalidation Matrix](#invalidation-matrix-rc-ready)).

### Expiration Strategies

#### Absolute Expiration

Cache entries expire after a fixed duration:

```csharp
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options => { ... });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(2));
});
```

#### Sliding Expiration

For custom cache managers, you can implement sliding expiration inside your `GetOrSetAsync` factory by configuring the underlying `IDistributedCache` entry options:

```csharp
// In a custom IDeliveryCacheManager.GetOrSetAsync implementation:
var options = new DistributedCacheEntryOptions
{
    SlidingExpiration = expiration  // Renewed on each access
};
await _cache.SetStringAsync(cacheKey, serialized, options, cancellationToken);
```

#### Per-query Expiration Override

You can override TTL for a specific cacheable query without changing the cache manager default:

```csharp
var itemResult = await client.GetItem<Article>("my-article")
    .WithCacheExpiration(TimeSpan.FromMinutes(5))
    .ExecuteAsync();

var listResult = await client.GetItems<Article>()
    .WithCacheExpiration(TimeSpan.FromMinutes(2))
    .ExecuteAsync();
```

Supported cacheable query builders:
- `GetItem<T>()`
- `GetItems<T>()`
- `GetType()`
- `GetTypes()`
- `GetTaxonomy()`
- `GetTaxonomies()`

#### Fail-safe

`IsFailSafeEnabled` lets the cache serve a stale copy when the origin cannot be reached: a request that got no response, or a status the SDK's own pipeline retries (`408`, `429`, `5xx`). Such a result carries `ResponseSource.FailSafe`. An answer from the API is never covered - an item that comes back `404` after being unpublished is dropped from the cache and the failure is returned - so unpublishing takes effect with fail-safe on, and the only content served stale is content the origin could not be asked about.

An invalidation and fail-safe compose the same way. `InvalidateAsync` expires the entries rather than deleting them when fail-safe is on, so a webhook followed by an outage serves the pre-webhook copy until the origin is back; a webhook followed by an answer drops it. `PurgeAsync(allowFailSafe: true)` keeps the same distinction.

## Cache Invalidation

### Invalidation Matrix (RC-ready)

Use this matrix when mapping webhook events to SDK dependency invalidation keys. Compose the detail keys with `DeliveryCacheDependencies` rather than by hand: they are the exact strings the SDK tags with, trimmed and lower-cased, and `InvalidateAsync` matches case-insensitively.

| Endpoint family | Detail dependency key | Listing scope dependency key |
|---|---|---|
| Items | `DeliveryCacheDependencies.ForItem(codename)` (`item_{codename}`) | `DeliveryCacheDependencies.ItemsListScope` (`scope_items_list`) |
| Types | `DeliveryCacheDependencies.ForType(codename)` (`type_{codename}`; also tags item/item-list caches containing items of that type) | `DeliveryCacheDependencies.TypesListScope` (`scope_types_list`) |
| Taxonomies | `DeliveryCacheDependencies.ForTaxonomy(codename)` (`taxonomy_{codename}`) | `DeliveryCacheDependencies.TaxonomiesListScope` (`scope_taxonomies_list`) |
| Assets | `DeliveryCacheDependencies.ForAsset(id)` (`asset_{id}`; tags every item cache whose asset elements or rich-text images reference it) | none - assets have no listing |

Recommended webhook pattern:
- item event: invalidate `ForItem(codename)` + `ItemsListScope`
- type event: invalidate `ForType(codename)` + `TypesListScope` — the type key covers both the cached type definition and every item/item-list cache whose payload references items of that type, so content-type changes or deletions do not require falling back to `ItemsListScope`
- taxonomy event: invalidate `ForTaxonomy(codename)` + `TaxonomiesListScope`
- asset event: invalidate `ForAsset(id)`

### Manual Invalidation

Resolve the manager first. The default client's resolves unkeyed, a named client's under its name, and a client from `DeliveryClient.Create` owns its container, so its manager is on the client:

```csharp
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;

// The default client
var cacheManager = serviceProvider.GetRequiredService<IDeliveryCacheManager>();

// A named client
var productionCacheManager = serviceProvider.GetRequiredKeyedService<IDeliveryCacheManager>("production");

// A client from DeliveryClient.Create owns its container, so the manager is on the client
var standaloneCacheManager = standaloneClient.CacheManager;
```

Then invalidate specific content (shown for the default client's manager):

```csharp
// Invalidate a specific item
await cacheManager.InvalidateAsync([DeliveryCacheDependencies.ForItem("homepage")]);

// Invalidate multiple entities at once
await cacheManager.InvalidateAsync([
    DeliveryCacheDependencies.ForItem("article1"),
    DeliveryCacheDependencies.ForItem("article2"),
    DeliveryCacheDependencies.ForTaxonomy("categories")]);

// Invalidate a specific type query dependency
await cacheManager.InvalidateAsync([DeliveryCacheDependencies.ForType("article")]);

// Invalidate all cached typed item-list queries
await cacheManager.InvalidateAsync([DeliveryCacheDependencies.ItemsListScope]);

// Invalidate all cached type/taxonomy listing queries
await cacheManager.InvalidateAsync([DeliveryCacheDependencies.TypesListScope]);
await cacheManager.InvalidateAsync([DeliveryCacheDependencies.TaxonomiesListScope]);
```

### Purge All (SDK Cache)

Sometimes you need to invalidate **everything at once** (e.g., after a deployment, emergency rollback, or a major content model change).

The SDK exposes an **optional** capability interface `IDeliveryCachePurger` that is implemented by built-in cache managers.

> [!NOTE]
> If you're using a custom cache manager that does not implement `IDeliveryCachePurger`, use provider-specific purge tooling or key-prefix rotation.

```csharp
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;

var cacheManager = serviceProvider.GetRequiredKeyedService<IDeliveryCacheManager>("production");

if (cacheManager is IDeliveryCachePurger purger)
{
    // Permanently remove all entries (default behavior)
    await purger.PurgeAsync();

    // Or: mark entries as logically expired, preserving fail-safe fallback data
    await purger.PurgeAsync(allowFailSafe: true);
}
```

### Webhook-Based Invalidation

Implement automatic cache invalidation using Kontent.ai webhooks:

#### 1. Webhook Controller

```csharp
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IServiceProvider serviceProvider,
        ILogger<WebhookController> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HttpPost("kontent")]
    public async Task<IActionResult> HandleWebhook([FromBody] WebhookNotification notification)
    {
        // Verify webhook signature (recommended)
        if (!VerifySignature(Request.Headers["X-KC-Signature"]))
        {
            return Unauthorized();
        }

        try
        {
            await ProcessWebhookAsync(notification);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook processing failed");
            return StatusCode(500);
        }
    }

    private async Task ProcessWebhookAsync(WebhookNotification notification)
    {
        // The default client's manager resolves unkeyed; a named client's under its name.
        var cacheManager = _serviceProvider.GetRequiredKeyedService<IDeliveryCacheManager>("production");
        var dependencies = new List<string>();

        foreach (var item in notification.Data.Items)
        {
            // Content item changes affect item queries and item listings.
            if (item.Type == "content_item")
            {
                dependencies.Add(DeliveryCacheDependencies.ForItem(item.Codename));
                dependencies.Add(DeliveryCacheDependencies.ItemsListScope);
            }

            // Taxonomy changes affect taxonomy queries and taxonomy listings.
            if (item.Type == "taxonomy")
            {
                dependencies.Add(DeliveryCacheDependencies.ForTaxonomy(item.Codename));
                dependencies.Add(DeliveryCacheDependencies.TaxonomiesListScope);
            }

            // Content type changes affect type queries and type listings.
            if (item.Type == "content_type")
            {
                dependencies.Add(DeliveryCacheDependencies.ForType(item.Codename));
                dependencies.Add(DeliveryCacheDependencies.TypesListScope);
            }

            // Asset changes affect every item that references the asset.
            if (item.Type == "asset")
            {
                dependencies.Add(DeliveryCacheDependencies.ForAsset(Guid.Parse(item.Id)));
            }
        }

        // Invalidate all affected cache entries
        await cacheManager.InvalidateAsync(dependencies.ToArray());

        _logger.LogInformation(
            "Invalidated {Count} cache entries from webhook",
            dependencies.Count);
    }

    private bool VerifySignature(string signature)
    {
        // Implement webhook signature verification
        // See: https://kontent.ai/learn/docs/webhooks/validate-webhooks
        return true;
    }
}

public class WebhookNotification
{
    public WebhookData Data { get; set; }
    public WebhookMessage Message { get; set; }
}

public class WebhookData
{
    public List<WebhookItem> Items { get; set; }
}

public class WebhookItem
{
    public string Id { get; set; }
    public string Codename { get; set; }
    public string Type { get; set; }
}

public class WebhookMessage
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Operation { get; set; }
}
```

#### 2. Webhook Signature Verification

```csharp
using System.Security.Cryptography;
using System.Text;

private bool VerifyWebhookSignature(string signature, string requestBody, string secret)
{
    if (string.IsNullOrEmpty(signature))
        return false;

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(requestBody));
    var computedSignature = Convert.ToBase64String(hash);

    return signature == computedSignature;
}

[HttpPost("kontent")]
public async Task<IActionResult> HandleWebhook()
{
    using var reader = new StreamReader(Request.Body);
    var body = await reader.ReadToEndAsync();

    var signature = Request.Headers["X-KC-Signature"].FirstOrDefault();
    var secret = _configuration["Kontent:WebhookSecret"];

    if (!VerifyWebhookSignature(signature, body, secret))
    {
        _logger.LogWarning("Invalid webhook signature");
        return Unauthorized();
    }

    var notification = JsonSerializer.Deserialize<WebhookNotification>(body);
    await ProcessWebhookAsync(notification);

    return Ok();
}
```

#### 3. Configure Webhook in Kontent.ai

1. Go to **Environment Settings** > **Webhooks**
2. Create a new webhook
3. Set URL to: `https://yourapp.com/api/webhooks/kontent`
4. Select events: "Publish", "Unpublish", "Archive"
5. Save the webhook secret for signature verification

### Timed Invalidation

For content that changes on a schedule:

```csharp
public class CacheInvalidationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheInvalidationService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cacheManager = _serviceProvider.GetRequiredKeyedService<IDeliveryCacheManager>("production");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Invalidate every cached items listing every 5 minutes.
                // Use DeliveryCacheDependencies.ItemsListScope ("scope_items_list")
                // to drop ALL items-list responses in one call. To target a single item,
                // use $"item_{codename}" instead.
                await cacheManager.InvalidateAsync(
                    [DeliveryCacheDependencies.ItemsListScope], stoppingToken);

                // Wait 5 minutes
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache invalidation failed");
            }
        }
    }
}

// Register service
services.AddHostedService<CacheInvalidationService>();
```

## Per-Client Caching

The SDK supports per-client cache configuration using keyed services, allowing different named clients to have independent caching strategies.

### Enabling Caching for Named Clients

Call `UseMemoryCache`, `UseHybridCache`, or `UseCacheManager` inside the registration of the client that should cache; clients registered without one stay uncached:

```csharp
// Production client: cached
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "production-environment-id";
    });
    delivery.UseMemoryCache(o =>
    {
        o.KeyPrefix = "prod";
        o.DefaultExpiration = TimeSpan.FromHours(1);
    });
});

services.AddDeliveryClient("preview", delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "preview-environment-id";
    options.UsePreviewApi = true;
    options.PreviewApiKey = "your-preview-api-key";
}));

// Preview client has no cache - always fetches fresh content
```

### Cache Key Prefixing

When multiple clients share the same underlying cache (e.g., same `IMemoryCache` or Redis instance), key prefixes prevent collisions:

```csharp
// Both clients share IMemoryCache but have isolated entries
services.AddDeliveryClient("client1", delivery =>
{
    delivery.Options.Configure(options => { ... });
    delivery.UseMemoryCache(o => o.KeyPrefix = "brand-a");
});

services.AddDeliveryClient("client2", delivery =>
{
    delivery.Options.Configure(options => { ... });
    delivery.UseMemoryCache(o => o.KeyPrefix = "brand-b");
});
```

Key prefixes are automatically applied to all cache keys and dependency tracking.

### Hybrid Cache for Named Clients

```csharp
// Register distributed cache implementation
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Register client
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "production-environment-id";
    });
    delivery.UseHybridCache(o =>
    {
        o.KeyPrefix = "prod";
        o.DefaultExpiration = TimeSpan.FromHours(2);
    });
});
```

## Multi-Tenant Caching

When serving multiple environments or brands, use per-client caching with distinct key prefixes:

### Complete Multi-Tenant Example

```csharp
// Register tenant clients, each with its own cache
services.AddDeliveryClient("tenant-a", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "tenant-a-environment-id";
    });
    delivery.UseMemoryCache(o => o.KeyPrefix = "tenant-a");
});

services.AddDeliveryClient("tenant-b", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "tenant-b-environment-id";
    });
    delivery.UseMemoryCache(o => o.KeyPrefix = "tenant-b");
});

// Access clients via factory
var factory = serviceProvider.GetRequiredService<IDeliveryClientFactory>();
var tenantAClient = factory.Get("tenant-a");
var tenantBClient = factory.Get("tenant-b");
```

### Per-Tenant Cache Invalidation

```csharp
public class TenantCacheService
{
    private readonly IServiceProvider _serviceProvider;

    public async Task InvalidateTenantCacheAsync(string tenantId, params string[] dependencies)
    {
        // Get the keyed cache manager for the specific tenant
        var cacheManager = _serviceProvider.GetKeyedService<IDeliveryCacheManager>(tenantId);

        if (cacheManager != null)
        {
            await cacheManager.InvalidateAsync(dependencies);
        }
    }
}
```

### Selective Caching (Production vs Preview)

A common pattern is to cache production content while preview stays fresh. Preview clients automatically bypass cache reads/writes:

```csharp
// Production: cached for performance
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
    });
    delivery.UseMemoryCache(o =>
    {
        o.KeyPrefix = "prod";
        o.DefaultExpiration = TimeSpan.FromHours(2);
    });
});

// Preview: no caching for fresh content during editing
services.AddDeliveryClient("preview", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
        options.UsePreviewApi = true;
        options.PreviewApiKey = "your-preview-api-key";
    });
    delivery.UseMemoryCache(); // Optional: preview still bypasses cache reads/writes
});
```

## Best Practices

### 1. Choose Appropriate Expiration Times

```csharp
// Inside each client's AddDeliveryClient callback:

// Frequently changing content (news, live data)
delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromMinutes(5));

// Moderately dynamic content (blog posts, products)
delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));

// Rarely changing content (about pages, navigation)
delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(6));

// Very stable content (archived content, documentation)
delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromDays(1));
```

### 2. Implement Webhook Invalidation

Always use webhooks in production to keep cache fresh:
- Set up webhook endpoint
- Verify signatures
- Invalidate specific dependencies
- Log invalidation events

### 3. Monitor Cache Performance

```csharp
public class MonitoredCacheManager : IDeliveryCacheManager
{
    private readonly IDeliveryCacheManager _inner;
    private readonly ILogger _logger;
    private readonly IMetrics _metrics;

    public async Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _inner.GetOrSetAsync(cacheKey, factory, expiration, cancellationToken);
        stopwatch.Stop();

        // FromFactory and IsStale are the only reliable classification: under eager refresh the factory
        // also runs for a background refresh, so a flag set inside it belongs to a different call.
        var outcome = result switch
        {
            null => "MISS",
            { FromFactory: true } => "FETCHED",
            { IsStale: true } => "STALE",
            _ => "HIT",
        };
        _metrics.RecordCacheAccess(result is { FromFactory: false }, stopwatch.ElapsedMilliseconds);
        _logger.LogDebug("Cache {Outcome} for key: {Key} in {Ms}ms", outcome, cacheKey, stopwatch.ElapsedMilliseconds);

        return result;
    }

    // ... implement InvalidateAsync delegation
}
```

### 4. Know What Happens When the Cache Fails

The built-in managers degrade rather than fail. A distributed cache that cannot be reached is worked around - the memory tier or the origin answers, and a two-second circuit breaker keeps a dead Redis from being retried on every request - and a serialization failure, which is a defect in the SDK's own payloads, is the one cache error that is thrown. Enable the `ZiggyCreatures.Caching.Fusion.FusionCache` log category to see outages, backplane failures and background-refresh errors as they are worked around; the SDK's own invalidation messages log under `Kontent.Ai.Delivery.Caching.FusionCacheManager`.

A custom manager owns that decision itself. If it wraps an `IDistributedCache`, catch the provider's exception, log it, and fall back to calling the factory so the query still answers.

### 5. Pre-Warm Cache

For critical content, pre-warm the cache on startup:

```csharp
public class CacheWarmupService : IHostedService
{
    private readonly IDeliveryClient _client;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Pre-load homepage
        await _client.GetItem("homepage").ExecuteAsync(cancellationToken);

        // Pre-load navigation
        await _client.GetItem("main_navigation").ExecuteAsync(cancellationToken);

        // Pre-load recent articles
        await _client.GetItems<Article>()
            .Where(f => f.System("type").IsEqualTo("article"))
            .OrderBy("system.last_modified", OrderingMode.Descending)
            .Limit(10)
            .ExecuteAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

services.AddHostedService<CacheWarmupService>();
```

### 6. Use Eager Refresh (Stale-While-Revalidate)

The built-in FusionCache-backed cache managers support eager refresh via `DeliveryCacheOptions.EagerRefreshThreshold`. When set, FusionCache proactively refreshes entries in the background before they expire:

```csharp
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options => { ... });
    delivery.UseMemoryCache(opts =>
    {
        opts.DefaultExpiration = TimeSpan.FromMinutes(30);
        opts.EagerRefreshThreshold = 0.8f; // Refresh at 80% of TTL (24 min)
    });
});
```

This returns the cached value immediately while refreshing in the background — no custom implementation needed.

### 7. Prevent Cache Stampede (Request Coalescing)

In high-traffic scenarios, a popular cache key can expire (or be invalidated) and cause many concurrent requests to miss the cache at the same time. If every request then calls the Delivery API, you get a spike of redundant calls (the "thundering herd" problem).

The SDK mitigates this for cached query execution by **coalescing concurrent cache misses**:
- The first request performs the API call and populates the cache
- Concurrent requests for the same cache key wait for the first request to finish (then read the cached result)

Implementation details:
- Coalescing is **scoped per `IDeliveryCacheManager` instance** (so different named clients / cache managers do not block each other)
- Coalescing uses an in-flight task registry per cache key (owner/waiter model), not per-key semaphores
- In-flight entries are removed immediately when the owner fetch completes (success or failure), so cleanup is completion-based

## Monitoring and Diagnostics

### Optional Redis Validation Suite

The SDK test project includes an opt-in Redis integration suite (`RedisCacheIntegrationTests`) that validates:
- item/type/taxonomy detail invalidation
- item/type/taxonomy listing scope invalidation
- cross-instance invalidation visibility using two service providers against the same Redis backend
- cross-instance invalidation in the ordering that needs a backplane: one instance caches an entry, the
  other reads it, and the first then invalidates. Without a backplane the second instance can keep
  serving the evicted entry; the test registers `AddFusionCacheStackExchangeRedisBackplane` and asserts
  the invalidation lands

Run it locally:

```bash
KONTENT_SDK_RUN_REDIS_TESTS=true \
KONTENT_SDK_REDIS_CONNECTION=localhost:6379 \
dotnet test Kontent.Ai.Delivery.Tests/Kontent.Ai.Delivery.Tests.csproj \
  --filter "FullyQualifiedName~RedisCacheIntegrationTests"
```

By default, the suite is skipped unless `KONTENT_SDK_RUN_REDIS_TESTS=true` is set.

### Cache Hit Rate

```csharp
public class CacheMetrics
{
    private long _hits;
    private long _misses;

    public double HitRate => _hits + _misses == 0
        ? 0
        : (double)_hits / (_hits + _misses);

    public void RecordHit() => Interlocked.Increment(ref _hits);
    public void RecordMiss() => Interlocked.Increment(ref _misses);
}
```

### Cache Size Monitoring

```csharp
services.AddMemoryCache(options =>
{
    options.TrackStatistics = true;  // Enable statistics tracking
});

// Access statistics
var cache = serviceProvider.GetRequiredService<IMemoryCache>();
var stats = cache.GetCurrentStatistics();

Console.WriteLine($"Total hits: {stats.TotalHits}");
Console.WriteLine($"Total misses: {stats.TotalMisses}");
Console.WriteLine($"Current entry count: {stats.CurrentEntryCount}");
```

### Logging

```csharp
public class LoggingCacheManager : IDeliveryCacheManager
{
    private readonly IDeliveryCacheManager _inner;
    private readonly ILogger _logger;

    public async Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var result = await _inner.GetOrSetAsync(cacheKey, factory, expiration, cancellationToken);

        // Read the classification off the result. A flag set inside the factory would be wrong under
        // eager refresh, where the factory runs in the background for a call that already returned.
        _logger.LogInformation("Cache {Result} for key: {Key}",
            result switch { null => "MISS", { FromFactory: true } => "MISS+SET", { IsStale: true } => "STALE", _ => "HIT" },
            cacheKey);

        return result;
    }

    public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
        => _inner.InvalidateAsync(dependencyKeys, cancellationToken);
}
```

## Troubleshooting

### Cache Not Working

**Problem**: Content is always fetched from API, not cache.

**Solutions**:

1. **Verify cache is registered**:
```csharp
var cacheManager = serviceProvider.GetKeyedService<IDeliveryCacheManager>("production");
if (cacheManager == null)
{
    // Cache not registered for "production"
}
```

2. **Check cache expiration** isn't too short
3. **Verify queries are identical** (different parameters = different cache keys)

### Stale Content

**Problem**: Cache returns old content after updates.

**Solutions**:

1. **Implement webhook invalidation**
2. **Reduce cache expiration time**
3. **Manually invalidate** after content updates

### Runtime Option Changes with Existing Cache

**Problem**: You changed `EnvironmentId`, `DefaultRenditionPreset` or `CustomAssetDomain` at runtime, but cached responses still reflect the previous setting. The environment id is read when the cache is created and is part of every key; the other two are baked into the hydrated objects a memory cache holds.

**Solutions**:

1. **Purge the client cache** after changing runtime options
2. **Recreate the client** if purging is not practical
3. **Prefer separate named clients + key prefixes** for production/preview/tenant/environment splits

### Memory Pressure

**Problem**: Application uses too much memory.

**Solutions**:

1. **Use hybrid cache** instead of memory cache
2. **Configure a size limit** - every entry the SDK writes counts as one unit, so this bounds the number of cached responses:
```csharp
services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024;
});
```
3. **Reduce expiration times**
4. **Be selective** about what you cache

### Redis Connection Failures

**Problem**: Redis is unavailable.

**What happens**: nothing is thrown out of a query. The distributed tier is worked around - the memory tier or the origin answers - and a two-second circuit breaker keeps the dead connection from being retried on every request; FusionCache re-syncs the tier when it is back. Reads cost an origin call more often while it lasts, and invalidations reach other nodes again only once it is over.

**Solutions**:

1. **See it**: enable the `ZiggyCreatures.Caching.Fusion.FusionCache` log category, which reports every worked-around failure at warning level.
2. **Configure connection resilience** so the client reconnects on its own:
```csharp
var config = ConfigurationOptions.Parse("localhost:6379");
config.AbortOnConnectFail = false;
config.ConnectRetry = 3;
config.ReconnectRetryPolicy = new ExponentialRetry(5000);
```

---

**Related Documentation**:
- [Main README](../README.md)
- [Performance Optimization Guide](performance-optimization.md)
- [Multi-Client Scenarios](multi-client-scenarios.md)
