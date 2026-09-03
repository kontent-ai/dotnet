using System.Diagnostics;
using System.Net;
using Kontent.Ai.Delivery.Api.Filtering;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.Caching;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="IItemsQuery{TModel}"/>
internal sealed class ItemsQuery<TModel>(
    IDeliveryApi api,
    ContentItemMapper contentItemMapper,
    ContentDeserializer contentDeserializer,
    ITypeProvider typeProvider,
    IDeliveryCacheManager? cacheManager,
    string? defaultRenditionPreset = null,
    Uri? customAssetDomain = null,
    ILogger? logger = null) : IItemsQuery<TModel>, ICacheExpirationConfigurable
{
    private readonly QueryLoggingHelper _log = new(logger, "Items", "list");
    private readonly SerializedFilterCollection _serializedFilters = [];
    private ListItemsParams _params = new();
    private bool _waitForLoadingNewContent;
    private bool _typeFilterApplied;
    public TimeSpan? CacheExpiration { get; set; }
    private static bool IsDynamicModel => ModelTypeHelper.IsDynamic<TModel>();

    public IItemsQuery<TModel> WithLanguage(string languageCodename, LanguageFallbackMode languageFallbackMode = LanguageFallbackMode.Enabled)
    {
        _params = _params with { Language = languageCodename };
        if (languageFallbackMode == LanguageFallbackMode.Disabled)
        {
            SystemFilterHelpers.AddSystemLanguageFilter(_serializedFilters, languageCodename);
        }
        return this;
    }

    public IItemsQuery<TModel> WithElements(params string[] elementCodenames)
    {
        _params = _params with { Elements = string.Join(",", elementCodenames) };
        return this;
    }

    public IItemsQuery<TModel> WithoutElements(params string[] elementCodenames)
    {
        _params = _params with { ExcludeElements = string.Join(",", elementCodenames) };
        return this;
    }

    public IItemsQuery<TModel> Depth(int depth)
    {
        _params = _params with { Depth = depth };
        return this;
    }

    public IItemsQuery<TModel> Skip(int skip)
    {
        _params = _params with { Skip = skip };
        return this;
    }

    public IItemsQuery<TModel> Limit(int limit)
    {
        _params = _params with { Limit = limit };
        return this;
    }

    public IItemsQuery<TModel> OrderBy(string elementOrAttributePath, OrderingMode orderingMode = OrderingMode.Ascending)
    {
        _params = _params with
        {
            OrderBy = orderingMode == OrderingMode.Ascending
                ? $"{elementOrAttributePath}[asc]"
                : $"{elementOrAttributePath}[desc]"
        };
        return this;
    }

    public IItemsQuery<TModel> WithTotalCount()
    {
        _params = _params with { IncludeTotalCount = true };
        return this;
    }

    public IItemsQuery<TModel> WaitForLoadingNewContent(bool enabled = true)
    {
        _waitForLoadingNewContent = enabled;
        return this;
    }

    public IItemsQuery<TModel> Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        build(new ItemsFilterBuilder(_serializedFilters));
        return this;
    }

    public async Task<IDeliveryResult<IDeliveryItemListingResponse<TModel>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        ApplyGenericTypeFilter();

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

    private async Task<IDeliveryResult<IDeliveryItemListingResponse<TModel>>> ExecuteWithCacheAsync(
        IDeliveryCacheManager cacheManager,
        Stopwatch? stopwatch,
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(cacheManager.StorageMode);

        var outcome = await CachedQueryExecutor.ExecuteAsync<DeliveryItemListingResponse<TModel>, DeliveryItemListingResponse<TModel>>(
            (captureApiResult, ct) => CachedItemsFetch.ExecuteAsync<DeliveryItemListingResponse<TModel>, DeliveryItemListingResponse<TModel>>(
                cacheManager,
                cacheKey,
                CacheExpiration,
                fetch: token => FetchFromApiAsync(waitForLoadingNewContent, token),
                captureApiResult,
                process: ProcessItemsAsync,
                toPayload: (response, _) => CachedRawItemsPayload.FromListing(response),
                rehydrate: (payload, token) => CachePayloadHelper.RehydrateListingAsync<TModel>(
                    payload,
                    contentDeserializer,
                    contentItemMapper,
                    IsDynamicModel,
                    defaultRenditionPreset,
                    customAssetDomain,
                    logger,
                    token),
                ct),
            cancellationToken).ConfigureAwait(false);

        var cached = outcome.Cached;

        if (outcome.Source is not CachedQuerySource.Fetched)
        {
            _log.LogQueryCompleted(stopwatch, HttpStatusCode.OK, cacheHit: true);

            return outcome.Source is CachedQuerySource.FailSafeHit
                ? DeliveryResult.FailSafeHit<IDeliveryItemListingResponse<TModel>>(
                    WithNextPageFetcher(cached!.Value), cached.DependencyKeys)
                : DeliveryResult.CacheHit<IDeliveryItemListingResponse<TModel>>(
                    WithNextPageFetcher(cached!.Value), cached.DependencyKeys);
        }

        var apiResult = QueryExecutionResultHelper.EnsureApiResult(outcome.ApiResult, "Items", "list");
        if (!apiResult.IsSuccess)
        {
            _log.LogQueryFailed(apiResult.StatusCode, apiResult.Error?.Message);
            _log.LogQueryCompleted(stopwatch, apiResult.StatusCode, cacheHit: false, apiResult.HasStaleContent);
            return CreateFailureResult(apiResult);
        }

        _log.LogQueryCompleted(stopwatch, apiResult.StatusCode, cacheHit: false, apiResult.HasStaleContent);
        var response = cached?.Value ?? apiResult.Value;
        return WrapSuccess(WithNextPageFetcher(response), apiResult, cached?.DependencyKeys);
    }

    private async Task<IDeliveryResult<IDeliveryItemListingResponse<TModel>>> ExecuteWithoutCacheAsync(
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

        var (resp, dependencyKeys) = await ProcessItemsAsync(deliveryResult.Value, cancellationToken).ConfigureAwait(false);
        _log.LogQueryCompleted(stopwatch, deliveryResult.StatusCode, cacheHit: false, deliveryResult.HasStaleContent);
        return WrapSuccess(WithNextPageFetcher(resp), deliveryResult, dependencyKeys);
    }

    private DeliveryItemListingResponse<TModel> WithNextPageFetcher(DeliveryItemListingResponse<TModel> resp)
        => resp with { NextPageFetcher = CreateNextPageFetcher(resp.Pagination) };

    private static IDeliveryResult<IDeliveryItemListingResponse<TModel>> WrapSuccess(
        DeliveryItemListingResponse<TModel> response,
        IDeliveryResult<DeliveryItemListingResponse<TModel>> apiResult,
        IReadOnlyList<string>? dependencyKeys) =>
        DeliveryResult.SuccessFrom<IDeliveryItemListingResponse<TModel>, DeliveryItemListingResponse<TModel>>(
            response, apiResult, dependencyKeys);

    private void ApplyGenericTypeFilter()
    {
        if (_typeFilterApplied)
            return;
        _typeFilterApplied = true;

        SystemFilterHelpers.AddGenericTypeFilter<TModel>(_serializedFilters, typeProvider, logger);
    }

    private async Task<IDeliveryResult<DeliveryItemListingResponse<TModel>>> FetchFromApiAsync(
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var rawResponse = await api.GetItemsInternalAsync<TModel>(
            _params,
            FilterQueryString.Render(_serializedFilters),
            waitForLoadingNewContent,
            cancellationToken).ConfigureAwait(false);
        return await rawResponse.ToDeliveryResultAsync(logger).ConfigureAwait(false);
    }

    private async Task<(DeliveryItemListingResponse<TModel> Response, string[] Dependencies)> ProcessItemsAsync(
        DeliveryItemListingResponse<TModel> resp, CancellationToken cancellationToken)
    {
        var items = resp.Items;
        var dependencyContext = new DependencyTrackingContext();

        if (items is { Count: > 0 })
        {
            foreach (var system in items.Select(item => item.System))
            {
                dependencyContext.TrackItem(system.Codename);
                dependencyContext.TrackItemType(system.Type);
            }
        }

        if (resp.ModularContent is not null)
        {
            foreach (var (codename, linkedItem) in resp.ModularContent)
            {
                // A component is invalidated through the item that owns it, so a key of its own is dead
                // weight. Its type still matters: the response does contain an item of that type.
                if (!ContentItemJsonHelper.IsComponent(linkedItem))
                {
                    dependencyContext.TrackItem(codename);
                }

                dependencyContext.TrackItemType(ContentItemJsonHelper.ExtractContentType(linkedItem));
            }
        }

        if (!IsDynamicModel)
        {
            foreach (var item in items)
            {
                await contentItemMapper.CompleteItemAsync(
                        item,
                        resp.ModularContent,
                        dependencyContext,
                        defaultRenditionPreset,
                        customAssetDomain,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return (resp, [.. dependencyContext.Dependencies, DeliveryCacheDependencies.ItemsListScope]);
    }

    private static IDeliveryResult<IDeliveryItemListingResponse<TModel>> CreateFailureResult(
        IDeliveryResult<DeliveryItemListingResponse<TModel>> deliveryResult) =>
        DeliveryResult.FailureFrom<IDeliveryItemListingResponse<TModel>, DeliveryItemListingResponse<TModel>>(deliveryResult);

    private Func<CancellationToken, Task<IDeliveryResult<IDeliveryItemListingResponse<TModel>>>>? CreateNextPageFetcher(IPagination pagination)
    {
        if (string.IsNullOrEmpty(pagination.NextPageUrl))
            return null;

        var nextSkip = OffsetPaginationHelper.GetNextSkip(pagination);
        var parametersSnapshot = _params;
        var waitForLoadingSnapshot = _waitForLoadingNewContent;
        var typeFilterAppliedSnapshot = _typeFilterApplied;
        var cacheExpirationSnapshot = CacheExpiration;
        var serializedFiltersSnapshot = _serializedFilters.Clone();

        return async (ct) =>
        {
            var nextQuery = new ItemsQuery<TModel>(api, contentItemMapper, contentDeserializer, typeProvider, cacheManager, defaultRenditionPreset, customAssetDomain, logger)
            {
                _params = parametersSnapshot with { Skip = nextSkip },
                _waitForLoadingNewContent = waitForLoadingSnapshot,
                _typeFilterApplied = typeFilterAppliedSnapshot,
                CacheExpiration = cacheExpirationSnapshot
            };

            nextQuery._serializedFilters.CopyFrom(serializedFiltersSnapshot);

            return await nextQuery.ExecuteAsync(ct).ConfigureAwait(false);
        };
    }

    private string BuildCacheKey(CacheStorageMode storageMode)
    {
        var modelType = storageMode == CacheStorageMode.RawJson ? null : typeof(TModel);
        return CacheKeyBuilder.BuildItemsKey(_params, _serializedFilters, modelType);
    }

}
