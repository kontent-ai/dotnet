# Performance Optimization Guide

This guide provides comprehensive strategies for optimizing the performance of applications using the Kontent.ai Delivery SDK, from query optimization to monitoring and diagnostics.

## Table of Contents

- [Overview](#overview)
- [Query Optimization](#query-optimization)
- [Caching Strategies](#caching-strategies)
- [Network Optimization](#network-optimization)
  - [HTTP Client Configuration](#http-client-configuration)
- [Parallel Operations](#parallel-operations)
- [Rate Limit Management](#rate-limit-management)
- [Monitoring and Diagnostics](#monitoring-and-diagnostics)
- [Production Best Practices](#production-best-practices)
- [Performance Benchmarks](#performance-benchmarks)
- [Troubleshooting](#troubleshooting)
  - [Slow Queries](#slow-queries)
  - [High Memory Usage](#high-memory-usage)
  - [Rate Limit Errors](#rate-limit-errors)
  - [Cache Misses](#cache-misses)

## Overview

Performance optimization for Kontent.ai applications involves:

1. **Minimizing API calls** through caching and efficient queries
2. **Reducing payload sizes** with projection and depth control
3. **Optimizing network usage** with proper HTTP client configuration
4. **Monitoring performance** to identify bottlenecks
5. **Managing rate limits** effectively

## Query Optimization

The shape of a query decides the size of the response, and payload dominates latency far more often
than anything else here.

- **Project.** `WithElements("title", "summary")` on a listing that renders titles cuts the payload
  by most of its weight - rich text and asset elements are the bulk of an item.
- **Keep depth low.** Each level of `Depth(n)` pulls in every linked item at that level, transitively.
  The default of 1 is right for most views; raise it only where a view genuinely renders the deeper level.
- **Use the feed for bulk.** `GetItemsFeed<T>()` streams with continuation tokens instead of holding a
  whole set in memory, which is what you want for indexing or export.
- **Filter server-side.** A `.Where(...)` clause is free; fetching everything and filtering in C# is not.

The syntax for all four is in the [querying guide](queries.md).

## Caching Strategies

Caching is the single largest performance lever, and it has its own guide:
[Caching](caching-guide.md). The parts that matter most for latency are
[expiration strategies](caching-guide.md#expiration-strategies),
[eager refresh](caching-guide.md#6-use-eager-refresh-stale-while-revalidate) for stale-while-revalidate
behaviour, and [request coalescing](caching-guide.md#7-prevent-cache-stampede-request-coalescing), which
keeps a cold key from sending one origin request per concurrent caller.

## Network Optimization

### HTTP Client Configuration

Configure HTTP client for optimal performance:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";

        // The ceiling on the whole call, retries included. Set it here, not through
        // HttpClient.Timeout, which the SDK owns and which would silently cap the retry sequence.
        options.Timeout = TimeSpan.FromSeconds(30);
    });

    delivery.HttpClient.ConfigureHttpClient(client =>
    {
        // Headers
        client.DefaultRequestHeaders.Add("User-Agent", "MyApp/1.0");
    });
});
```

The SDK builds its transport on `IHttpClientFactory`, so connection pooling and handler rotation are
already handled — there is nothing to configure and no `HttpClient` to own. Retry and backoff are
covered under [Rate Limit Management](#rate-limit-management).

## Parallel Operations

`IDeliveryClient` is thread-safe and intended to be a singleton, so independent queries can run
concurrently:

```csharp
var articlesTask = client.GetItems<Article>().Limit(10).ExecuteAsync(cancellationToken);
var productsTask = client.GetItems<Product>().Limit(10).ExecuteAsync(cancellationToken);

await Task.WhenAll(articlesTask, productsTask);

var articles = (await articlesTask).Value.Items;
var products = (await productsTask).Value.Items;
```

Concurrency counts against the rate limit, so fan-out is bounded by the section below rather than by
the SDK.

## Rate Limit Management

The Delivery API rate-limits by requests per second, burst capacity and monthly quota; the current
figures are in the [Kontent.ai documentation](https://kontent.ai/learn/docs/apis/delivery-api).

**The SDK already handles a `429`**: the default pipeline retries with exponential backoff and honours
`Retry-After`. Sustained over-limit traffic still surfaces as a failed result.

To change how hard it tries, replace the pipeline:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.ConfigureResilience(pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 5,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
    }));
});
```

> [!NOTE]
> Replacing the pipeline also replaces its per-attempt timeout, and `DeliveryOptions.Timeout` then becomes the only bound on the call. Read [Timeouts](../README.md#timeouts) before changing this - a longer retry sequence inside an unchanged ceiling just fails later.

Retrying is the last defence, not the first. In order of effect:

1. **Cache.** A cache hit is a request that never happened.
2. **Use `GetItemsFeed<T>()` for bulk**, rather than many small queries.
3. **Project and filter**, so one query answers what would otherwise take several.
4. **Watch `429` rates** - the `TimingHandler` below sees every request, retries included.

## Monitoring and Diagnostics

Time the transport, not the client. A `DelegatingHandler` on the builder's `HttpClient` sees every
request, retries included:

```csharp
public sealed class TimingHandler(ILogger<TimingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(start);

        logger.Log(elapsed > TimeSpan.FromSeconds(1) ? LogLevel.Warning : LogLevel.Information,
            "{Method} {Uri} -> {Status} in {ElapsedMs}ms",
            request.Method, request.RequestUri, (int)response.StatusCode, elapsed.TotalMilliseconds);

        return response;
    }
}
```

```csharp
services.AddTransient<TimingHandler>();

services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.HttpClient.AddHttpMessageHandler<TimingHandler>();
});
```

The handler sits inside the SDK's own, so a retried request runs it again.

To tell a cached response from a fetched one, read
[`ResponseSource`](caching-guide.md#detecting-cache-hits) off the result rather than timing it.

## Production Best Practices

- **Cache.** It is the first defence against both latency and the rate limit.
- **Keep the client a singleton.** Registering it per request defeats connection pooling; the DI
  registration already makes it one.
- **Set `DeliveryOptions.Timeout`** if something upstream budgets on how long a call may take. Without
  it the default pipeline bounds each attempt but not the call as a whole - see the
  [README](../README.md#timeouts).
- **Watch `429` rates**, not just latency. Rate limiting shows up as retries and tail latency long
  before it shows up as errors.

## Performance Benchmarks

This guide publishes no numbers: they depend on your content, payload size, region and cache
configuration, and a figure measured elsewhere tells you nothing about your application.

The repository carries a [BenchmarkDotNet
project](https://github.com/kontent-ai/dotnet/tree/main/src/delivery/Kontent.Ai.Delivery.Benchmarks)
that measures the SDK's own overhead — deserialization, mapping, rich-text parsing — against recorded
fixtures over a mock transport, so the network is out of the picture:

```bash
dotnet run -c Release --project src/delivery/Kontent.Ai.Delivery.Benchmarks
```

For end-to-end numbers, measure your own endpoints with the
[`TimingHandler`](#monitoring-and-diagnostics) above, and read
[`ResponseSource`](caching-guide.md#detecting-cache-hits) to know which tier answered before drawing a
conclusion from a duration.

## Troubleshooting

### Slow Queries

**Problem**: Queries take several seconds.

**Solutions**:

1. **Enable caching**
2. **Reduce depth**: `Depth(0)` or `Depth(1)`
3. **Limit elements**: Use `WithElements()`
4. **Optimize filters**: Use system properties
5. **Check network**: Verify connectivity and latency

### High Memory Usage

**Problem**: Application using too much memory.

**Solutions**:

1. **Bound the cache.** `AddMemoryCache(o => o.SizeLimit = …)` does *not* size the SDK's cache — the
   built-ins are FusionCache-backed, so their memory tier is configured through
   [`ConfigureFusionCache`](caching-guide.md#reaching-into-fusioncache). Shorten
   `DefaultExpiration` first; it is the blunter and more reliable lever.
2. **Switch to `UseHybridCache`**, which keeps the bulk in the distributed tier.
3. **Project and lower depth** — a cached response is as large as the payload that produced it.

### Rate Limit Errors

**Problem**: Receiving 429 (Too Many Requests) errors.

**Solutions**:

1. **Implement caching** (primary solution)
2. **Reduce API calls** through batching
3. **Use items feed** for bulk operations
4. **Add retry policies** with backoff
5. **Contact Kontent.ai** to increase limits if needed

### Cache Misses

**Problem**: Low cache hit rate.

**Solutions**:

1. **Increase cache expiration** time
2. **Warm cache** on startup
3. **Verify cache is configured** correctly
4. **Check query consistency** (different parameters = different cache keys)

---

**Related Documentation**:
- [Main README](../README.md)
- [Caching Guide](caching-guide.md)
- [Querying](queries.md)
- [Multi-Client Scenarios](multi-client-scenarios.md)
