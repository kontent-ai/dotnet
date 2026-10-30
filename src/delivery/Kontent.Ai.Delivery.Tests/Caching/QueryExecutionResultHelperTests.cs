using System.Net;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.SharedModels;
using Kontent.Ai.Delivery.Tests.Models.ContentTypes;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.Caching;

public class QueryExecutionResultHelperTests
{
    [Fact]
    public void CreateMissingApiResultFailure_ReportsNoResponseAndNamesTheCacheManager()
    {
        var result = QueryExecutionResultHelper.CreateMissingApiResultFailure<string>("Items", "list");

        Assert.False(result.IsSuccess);
        Assert.Equal(default, result.StatusCode);
        Assert.Contains("IDeliveryCacheManager", result.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureApiResult_WhenNull_ReturnsMissingApiResultFailure()
    {
        var result = QueryExecutionResultHelper.EnsureApiResult<string>(null, "Items", "list");

        Assert.False(result.IsSuccess);
        Assert.Equal(default, result.StatusCode);
    }

    [Fact]
    public async Task CachedQuery_CacheManagerSkipsTheFactory_FailsWithoutAnInventedStatus()
    {
        var env = Guid.NewGuid().ToString();
        var mock = new MockHttpMessageHandler();
        var request = mock.When($"https://deliver.kontent.ai/{env}/items/coffee").Respond("application/json", "{}");
        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, delivery =>
        {
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock);
            delivery.UseCacheManager(_ => new FactorySkippingCacheManager());
        });
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDeliveryClient>().GetItem<Article>("coffee").ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(default, result.StatusCode);
        Assert.Contains("IDeliveryCacheManager", result.Error?.Message, StringComparison.Ordinal);
        Assert.Equal(0, mock.GetMatchCount(request));
    }

    // Breaks the documented protocol: a miss that never asks the origin.
    private sealed class FactorySkippingCacheManager : IDeliveryCacheManager
    {
        public Task<CacheResult<T>?> GetOrSetAsync<T>(string cacheKey, Func<CancellationToken, Task<CacheEntry<T>?>> factory,
            TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
            => Task.FromResult<CacheResult<T>?>(null);

        public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    [Fact]
    public void EnsureApiResult_WhenPresent_ReturnsOriginalResult()
    {
        var existing = DeliveryResult.Success(
            "value",
            requestUrl: "/items",
            statusCode: HttpStatusCode.OK,
            hasStaleContent: false,
            responseHeaders: null,
            responseSource: ResponseSource.Origin);

        var result = QueryExecutionResultHelper.EnsureApiResult<string>(existing, "Items", "list");

        Assert.Same(existing, result);
    }
}
