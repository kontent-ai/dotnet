using System.Diagnostics;
using System.Net;
using Kontent.Ai.Delivery.Api.Filtering;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.Caching;
using Kontent.Ai.Delivery.TaxonomyGroups;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="ITaxonomiesQuery"/>
internal sealed class TaxonomiesQuery(
    IDeliveryApi api,
    IDeliveryCacheManager? cacheManager,
    ILogger? logger = null) : ITaxonomiesQuery, ICacheExpirationConfigurable
{
    private readonly QueryLoggingHelper _log = new(logger, "Taxonomies", "list");
    private readonly SerializedFilterCollection _serializedFilters = [];
    private ListTaxonomyGroupsParams _params = new();
    private bool _waitForLoadingNewContent;
    public TimeSpan? CacheExpiration { get; set; }

    public ITaxonomiesQuery Skip(int skip)
    {
        _params = _params with { Skip = skip };
        return this;
    }

    public ITaxonomiesQuery Limit(int limit)
    {
        _params = _params with { Limit = limit };
        return this;
    }

    public ITaxonomiesQuery Where(Func<ITaxonomiesFilterBuilder, ITaxonomiesFilterBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        build(new TaxonomiesFilterBuilder(_serializedFilters));
        return this;
    }

    public ITaxonomiesQuery WaitForLoadingNewContent(bool enabled = true)
    {
        _waitForLoadingNewContent = enabled;
        return this;
    }

    public async Task<IDeliveryResult<IDeliveryTaxonomyListingResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _log.LogQueryStarting();
        var stopwatch = _log.StartTimingIfEnabled();
        bool? waitForLoadingNewContent = _waitForLoadingNewContent ? true : null;
        var shouldBypassCache = _waitForLoadingNewContent;

        return cacheManager is not null && !shouldBypassCache
            ? await ExecuteWithCacheAsync(
                cacheManager,
                stopwatch,
                waitForLoadingNewContent,
                cancellationToken).ConfigureAwait(false)
            : await ExecuteWithoutCacheAsync(stopwatch, waitForLoadingNewContent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IDeliveryResult<IDeliveryTaxonomyListingResponse>> ExecuteWithCacheAsync(
        IDeliveryCacheManager cacheManager,
        Stopwatch? stopwatch,
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyBuilder.BuildTaxonomiesKey(_params, _serializedFilters);

        var outcome = await CachedQueryExecutor.ExecuteAsync<DeliveryTaxonomyListingResponse, DeliveryTaxonomyListingResponse>(
            cacheManager,
            cacheKey,
            (captureApiResult, ct) => cacheManager.GetOrSetAsync(
                cacheKey,
                async factoryToken =>
                {
                    var result = await FetchFromApiAsync(waitForLoadingNewContent, factoryToken).ConfigureAwait(false);
                    captureApiResult(result);
                    if (!result.IsSuccess)
                    {
                        CachedQueryExecutor.ThrowIfOriginUnavailable(result);
                        return null;
                    }

                    return new CacheEntry<DeliveryTaxonomyListingResponse>(result.Value, BuildDependencies(result.Value.Taxonomies));
                },
                CacheExpiration,
                ct),
            cancellationToken).ConfigureAwait(false);

        var cached = outcome.Cached;

        if (outcome.Source is not CachedQuerySource.Fetched)
        {
            _log.LogQueryCompleted(stopwatch, HttpStatusCode.OK, cacheHit: true);

            return outcome.Source is CachedQuerySource.FailSafeHit
                ? DeliveryResult.FailSafeHit<IDeliveryTaxonomyListingResponse>(
                    WithNextPageFetcher(cached!.Value), cached.DependencyKeys)
                : DeliveryResult.CacheHit<IDeliveryTaxonomyListingResponse>(
                    WithNextPageFetcher(cached!.Value), cached.DependencyKeys);
        }

        var apiResult = QueryExecutionResultHelper.EnsureApiResult(outcome.ApiResult, "Taxonomies", "list");

        if (!apiResult.IsSuccess)
        {
            _log.LogQueryFailed(apiResult.StatusCode, apiResult.Error?.Message);
            _log.LogQueryCompleted(stopwatch, apiResult.StatusCode, cacheHit: false, apiResult.HasStaleContent);
            return CreateFailureResult(apiResult);
        }

        _log.LogQueryCompleted(stopwatch, apiResult.StatusCode, cacheHit: false, apiResult.HasStaleContent);
        var response = cached?.Value ?? apiResult.Value;
        return WrapSuccess(
            WithNextPageFetcher(response),
            apiResult,
            cached?.DependencyKeys ?? BuildDependencies(response.Taxonomies));
    }

    private async Task<IDeliveryResult<IDeliveryTaxonomyListingResponse>> ExecuteWithoutCacheAsync(
        Stopwatch? stopwatch,
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var deliveryResult = await FetchFromApiAsync(waitForLoadingNewContent, cancellationToken).ConfigureAwait(false);
        if (!deliveryResult.IsSuccess)
        {
            _log.LogQueryFailed(deliveryResult.StatusCode, deliveryResult.Error?.Message);
            _log.LogQueryCompleted(stopwatch, deliveryResult.StatusCode, cacheHit: false, deliveryResult.HasStaleContent);
            return CreateFailureResult(deliveryResult);
        }

        _log.LogQueryCompleted(stopwatch, deliveryResult.StatusCode, cacheHit: false, deliveryResult.HasStaleContent);
        return WrapSuccess(
            WithNextPageFetcher(deliveryResult.Value),
            deliveryResult,
            BuildDependencies(deliveryResult.Value.Taxonomies));
    }

    private async Task<IDeliveryResult<DeliveryTaxonomyListingResponse>> FetchFromApiAsync(
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var response = await api.GetTaxonomiesInternalAsync(
            _params,
            FilterQueryString.Render(_serializedFilters),
            waitForLoadingNewContent,
            cancellationToken).ConfigureAwait(false);
        return await response.ToDeliveryResultAsync(logger).ConfigureAwait(false);
    }

    private static string[] BuildDependencies(IReadOnlyList<TaxonomyGroup> taxonomies)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DeliveryCacheDependencies.TaxonomiesListScope
        };

        foreach (var taxonomy in taxonomies)
        {
            var dependency = CacheDependencyKeyBuilder.BuildTaxonomyDependencyKey(taxonomy.System.Codename);
            if (dependency is null)
                continue;

            dependencies.Add(dependency);
        }

        return [.. dependencies];
    }

    private static IDeliveryResult<IDeliveryTaxonomyListingResponse> WrapSuccess(
        DeliveryTaxonomyListingResponse response,
        IDeliveryResult<DeliveryTaxonomyListingResponse> apiResult,
        IReadOnlyList<string> dependencyKeys)
        => DeliveryResult.SuccessFrom<IDeliveryTaxonomyListingResponse, DeliveryTaxonomyListingResponse>(
            response, apiResult, dependencyKeys);

    private static IDeliveryResult<IDeliveryTaxonomyListingResponse> CreateFailureResult(
        IDeliveryResult<DeliveryTaxonomyListingResponse> deliveryResult)
        => DeliveryResult.FailureFrom<IDeliveryTaxonomyListingResponse, DeliveryTaxonomyListingResponse>(deliveryResult);

    private DeliveryTaxonomyListingResponse WithNextPageFetcher(DeliveryTaxonomyListingResponse response)
        => response with { NextPageFetcher = CreateNextPageFetcher(response.Pagination) };

    private Func<CancellationToken, Task<IDeliveryResult<IDeliveryTaxonomyListingResponse>>>? CreateNextPageFetcher(IPagination pagination)
    {
        if (string.IsNullOrEmpty(pagination.NextPageUrl))
            return null;

        var nextSkip = OffsetPaginationHelper.GetNextSkip(pagination);
        var parametersSnapshot = _params;
        var waitForLoadingSnapshot = _waitForLoadingNewContent;
        var cacheExpirationSnapshot = CacheExpiration;
        var serializedFiltersSnapshot = _serializedFilters.Clone();

        return ct => CreateNextPageQuery(
                nextSkip,
                parametersSnapshot,
                waitForLoadingSnapshot,
                cacheExpirationSnapshot,
                serializedFiltersSnapshot)
            .ExecuteAsync(ct);
    }

    private TaxonomiesQuery CreateNextPageQuery(
        int nextSkip,
        ListTaxonomyGroupsParams parametersSnapshot,
        bool waitForLoadingSnapshot,
        TimeSpan? cacheExpirationSnapshot,
        SerializedFilterCollection serializedFiltersSnapshot)
    {
        var nextQuery = new TaxonomiesQuery(api, cacheManager, logger)
        {
            _params = parametersSnapshot with { Skip = nextSkip },
            _waitForLoadingNewContent = waitForLoadingSnapshot,
            CacheExpiration = cacheExpirationSnapshot
        };

        nextQuery._serializedFilters.CopyFrom(serializedFiltersSnapshot);
        return nextQuery;
    }

}
