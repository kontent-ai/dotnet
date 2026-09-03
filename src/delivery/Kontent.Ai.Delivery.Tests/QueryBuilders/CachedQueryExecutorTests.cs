using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.SharedModels;

namespace Kontent.Ai.Delivery.Tests.QueryBuilders;

/// <summary>
/// Pins how a cached query's result is classified. Under eager refresh the factory also runs on a
/// background thread while the stale-but-valid value is returned immediately, so anything it records may
/// belong to a different call than the one reading it; only what the cache says about the value counts.
/// </summary>
public class CachedQueryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_BackgroundRefreshWroteAnApiResult_StillReportsACacheHit()
    {
        // The shape of an eager-refresh hit: the cache returns a stored value (FromFactory false) while a
        // background factory writes into the same captured local this call reads. That local says nothing
        // about the value being returned.
        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
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
    public async Task ExecuteAsync_ServedStale_ReportsFailSafe()
    {
        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            (_, _) => Task.FromResult<CacheResult<string>?>(
                new CacheResult<string>("stale", []) { FromFactory = false, IsStale = true }),
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.FailSafeHit, outcome.Source);
    }

    [Fact]
    public async Task ExecuteAsync_FactoryProducedTheValue_ReportsFetchedAndKeepsTheApiResult()
    {
        var apiResult = SuccessfulApiResult();

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            (captureApiResult, _) =>
            {
                captureApiResult(apiResult);
                return Task.FromResult<CacheResult<string>?>(
                    new CacheResult<string>("fresh", []) { FromFactory = true });
            },
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.Fetched, outcome.Source);
        Assert.Same(apiResult, outcome.ApiResult);
    }

    [Fact]
    public async Task ExecuteAsync_NothingCached_ReportsFetchedSoTheFailureSurfaces()
    {
        var failure = DeliveryResult.Failure<string>("url", System.Net.HttpStatusCode.NotFound, new Error());

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            (captureApiResult, _) =>
            {
                captureApiResult(failure);
                return Task.FromResult<CacheResult<string>?>(null);
            },
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.Fetched, outcome.Source);
        Assert.Same(failure, outcome.ApiResult);
    }

    [Fact]
    public async Task ExecuteAsync_OriginUnavailableAndNothingStale_ReportsFetchedWithTheCarriedFailure()
    {
        // The factory threw for an outage and the manager had no stale copy, so the exception surfaces
        // here carrying the failed result - which is the answer even for a caller whose own factory never
        // ran because it was waiting on the same key.
        var failure = DeliveryResult.Failure<string>("url", System.Net.HttpStatusCode.ServiceUnavailable, new Error());

        var outcome = await CachedQueryExecutor.ExecuteAsync<string, string>(
            (_, _) => throw new OriginUnavailableException(failure),
            CancellationToken.None);

        Assert.Equal(CachedQuerySource.Fetched, outcome.Source);
        Assert.Null(outcome.Cached);
        Assert.Same(failure, outcome.ApiResult);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.NotFound, false)]
    [InlineData(System.Net.HttpStatusCode.Forbidden, false)]
    [InlineData(System.Net.HttpStatusCode.BadRequest, false)]
    [InlineData(default(System.Net.HttpStatusCode), true)]
    [InlineData(System.Net.HttpStatusCode.RequestTimeout, true)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, true)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, true)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, true)]
    public void ThrowIfOriginUnavailable_ThrowsForOutagesOnly(System.Net.HttpStatusCode status, bool isOutage)
    {
        var failure = DeliveryResult.Failure<string>("url", status, new Error());

        var act = () => CachedQueryExecutor.ThrowIfOriginUnavailable(failure);

        if (isOutage)
        {
            var thrown = Assert.Throws<OriginUnavailableException>(act);
            Assert.Same(failure, thrown.Result);
        }
        else
        {
            act();
        }
    }

    private static IDeliveryResult<string> SuccessfulApiResult() =>
        DeliveryResult.Success("value", "url", System.Net.HttpStatusCode.OK, false, null, ResponseSource.Origin);
}
