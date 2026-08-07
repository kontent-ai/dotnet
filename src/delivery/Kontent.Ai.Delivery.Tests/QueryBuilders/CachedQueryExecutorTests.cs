using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.Caching;
using Kontent.Ai.Delivery.SharedModels;

namespace Kontent.Ai.Delivery.Tests.QueryBuilders;

/// <summary>
/// Pins how a cached query's result is classified. Under eager refresh the factory also runs on a
/// background thread while the stale-but-valid value is returned immediately, so anything it records may
/// belong to a different call than the one reading it.
/// </summary>
public class CachedQueryExecutorTests
{
    private const string Key = "items|list";

    [Fact]
    public async Task ExecuteAsync_BackgroundRefreshWroteAnApiResult_StillReportsACacheHit()
    {
        // The shape of an eager-refresh hit: the cache returns a stored value (FromFactory false) while a
        // background factory writes into the same captured local this call reads. That local says nothing
        // about the value being returned.
        var manager = new StubCacheManager(failSafeActive: false);

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            manager,
            Key,
            (captureApiResult, _) =>
            {
                captureApiResult(SuccessfulApiResult());
                return Task.FromResult<CacheResult<string>?>(
                    new CacheResult<string>("cached", []) { FromFactory = false });
            },
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.CacheHit, outcome.Source);
        Assert.Null(outcome.ApiResult);
    }

    [Fact]
    public async Task ExecuteAsync_ServedStaleWhileFailSafeActive_ReportsFailSafe()
    {
        var manager = new StubCacheManager(failSafeActive: true);

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            manager,
            Key,
            (_, _) => Task.FromResult<CacheResult<string>?>(
                new CacheResult<string>("stale", []) { FromFactory = false }),
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.FailSafeHit, outcome.Source);
    }

    [Fact]
    public async Task ExecuteAsync_FactoryProducedTheValue_ReportsFetchedAndKeepsTheApiResult()
    {
        var manager = new StubCacheManager(failSafeActive: true);
        var apiResult = SuccessfulApiResult();

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            manager,
            Key,
            (captureApiResult, _) =>
            {
                captureApiResult(apiResult);
                return Task.FromResult<CacheResult<string>?>(
                    new CacheResult<string>("fresh", []) { FromFactory = true });
            },
            CancellationToken.None);

        // Fail-safe state is irrelevant once this call's own factory produced the value.
        Assert.Equal(CachedQuerySource.Fetched, outcome.Source);
        Assert.Same(apiResult, outcome.ApiResult);
    }

    [Fact]
    public async Task ExecuteAsync_NothingCached_ReportsFetchedSoTheFailureSurfaces()
    {
        var manager = new StubCacheManager(failSafeActive: false);
        var failure = DeliveryResult.Failure<string>("url", System.Net.HttpStatusCode.NotFound, new Error());

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            manager,
            Key,
            (captureApiResult, _) =>
            {
                captureApiResult(failure);
                return Task.FromResult<CacheResult<string>?>(null);
            },
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.Fetched, outcome.Source);
        Assert.Same(failure, outcome.ApiResult);
    }

    private static IDeliveryResult<string> SuccessfulApiResult() =>
        DeliveryResult.Success("value", "url", System.Net.HttpStatusCode.OK, false, null, ResponseSource.Origin);

    private sealed class StubCacheManager(bool failSafeActive) : IDeliveryCacheManager, IFailSafeStateProvider
    {
        public bool IsFailSafeActive(string cacheKey) => failSafeActive;

        public Task<CacheResult<T>?> GetOrSetAsync<T>(
            string cacheKey,
            Func<CancellationToken, Task<CacheEntry<T>?>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) where T : class =>
            throw new NotSupportedException("The executor is handed the cache call, it does not make one.");

        public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
