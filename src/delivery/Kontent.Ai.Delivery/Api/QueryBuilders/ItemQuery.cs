using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.Caching;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="IItemQuery{TModel}"/>
internal sealed class ItemQuery<TModel>(
    IDeliveryApi api,
    string codename,
    ContentItemMapper contentItemMapper,
    ContentDeserializer contentDeserializer,
    IDeliveryCacheManager? cacheManager,
    string? defaultRenditionPreset = null,
    Uri? customAssetDomain = null,
    ILogger? logger = null) : IItemQuery<TModel>, ICacheExpirationConfigurable
{
    private readonly QueryLoggingHelper _log = new(logger, "Item", codename);
    private SingleItemParams _params = new();
    private bool _waitForLoadingNewContent;
    public TimeSpan? CacheExpiration { get; set; }
    private static bool IsDynamicModel => ModelTypeHelper.IsDynamic<TModel>();
    internal IReadOnlyDictionary<string, JsonElement>? LatestModularContent { get; private set; }

    public IItemQuery<TModel> WithLanguage(string languageCodename)
    {
        _params = _params with { Language = languageCodename };
        return this;
    }

    public IItemQuery<TModel> WithElements(params string[] elementCodenames)
    {
        _params = _params with { Elements = string.Join(",", elementCodenames) };
        return this;
    }

    public IItemQuery<TModel> WithoutElements(params string[] elementCodenames)
    {
        _params = _params with { ExcludeElements = string.Join(",", elementCodenames) };
        return this;
    }

    public IItemQuery<TModel> Depth(int depth)
    {
        _params = _params with { Depth = depth };
        return this;
    }

    public IItemQuery<TModel> WaitForLoadingNewContent(bool enabled = true)
    {
        _waitForLoadingNewContent = enabled;
        return this;
    }

    public async Task<IDeliveryResult<IContentItem<TModel>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        LatestModularContent = null;
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

    private async Task<IDeliveryResult<IContentItem<TModel>>> ExecuteWithCacheAsync(
        IDeliveryCacheManager cacheManager,
        Stopwatch? stopwatch,
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(cacheManager.StorageMode);

        var outcome = await CachedQueryExecutor.ExecuteAsync<IContentItem<TModel>, DeliveryItemResponse<TModel>>(
            (captureApiResult, ct) => CachedItemsFetch.ExecuteAsync<IContentItem<TModel>, DeliveryItemResponse<TModel>>(
                cacheManager,
                cacheKey,
                CacheExpiration,
                fetch: token => FetchFromApiAsync(waitForLoadingNewContent, token),
                captureApiResult,
                process: ProcessItemAsync,
                toPayload: (item, response) => CachedRawItemsPayload.FromItem(item, response.ModularContent),
                rehydrate: async (payload, token) => await CachePayloadHelper.RehydrateItemAsync<TModel>(
                    payload,
                    contentDeserializer,
                    contentItemMapper,
                    IsDynamicModel,
                    defaultRenditionPreset,
                    customAssetDomain,
                    logger,
                    token).ConfigureAwait(false),
                ct),
            cancellationToken).ConfigureAwait(false);

        var cached = outcome.Cached;

        if (outcome.Source is not CachedQuerySource.Fetched)
        {
            _log.LogQueryCompleted(stopwatch, HttpStatusCode.OK, cacheHit: true);

            return outcome.Source is CachedQuerySource.FailSafeHit
                ? DeliveryResult.FailSafeHit(cached!.Value, cached.DependencyKeys)
                : DeliveryResult.CacheHit(cached!.Value, cached.DependencyKeys);
        }

        var apiResult = QueryExecutionResultHelper.EnsureApiResult(outcome.ApiResult, "Item", codename);
        if (!apiResult.IsSuccess)
        {
            _log.LogQueryFailed(apiResult.StatusCode, apiResult.Error?.Message);
            _log.LogQueryCompleted(stopwatch, apiResult.StatusCode, cacheHit: false, apiResult.HasStaleContent);
            return CreateFailureResult(apiResult);
        }

        _log.LogQueryCompleted(stopwatch, apiResult.StatusCode, cacheHit: false, apiResult.HasStaleContent);
        return WrapSuccess(cached?.Value ?? apiResult.Value.Item, apiResult, cached?.DependencyKeys);
    }

    private async Task<IDeliveryResult<IContentItem<TModel>>> ExecuteWithoutCacheAsync(
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

        var (item, dependencyKeys) = await ProcessItemAsync(deliveryResult.Value, cancellationToken).ConfigureAwait(false);
        _log.LogQueryCompleted(stopwatch, deliveryResult.StatusCode, cacheHit: false, deliveryResult.HasStaleContent);
        return WrapSuccess(item, deliveryResult, dependencyKeys);
    }

    private static IDeliveryResult<IContentItem<TModel>> WrapSuccess(
        IContentItem<TModel> item,
        IDeliveryResult<DeliveryItemResponse<TModel>> apiResult,
        IReadOnlyList<string>? dependencyKeys) =>
        DeliveryResult.SuccessFrom(item, apiResult, dependencyKeys);

    private async Task<IDeliveryResult<DeliveryItemResponse<TModel>>> FetchFromApiAsync(
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken = default)
    {
        var rawResponse = await api.GetItemInternalAsync<TModel>(
                codename,
                _params,
                // A single-item query carries no filters; the parameter exists for the listing endpoints.
                filters: null,
                waitForLoadingNewContent,
                cancellationToken)
            .ConfigureAwait(false);
        return await rawResponse.ToDeliveryResultAsync(logger).ConfigureAwait(false);
    }

    private async Task<(IContentItem<TModel> Item, string[] Dependencies)> ProcessItemAsync(
        DeliveryItemResponse<TModel> resp, CancellationToken cancellationToken)
    {
        LatestModularContent = resp.ModularContent;
        var item = resp.Item;
        var dependencyContext = new DependencyTrackingContext();

        dependencyContext.TrackItem(item.System.Codename);
        dependencyContext.TrackItemType(item.System.Type);
        if (resp.ModularContent is not null)
        {
            foreach (var (itemCodename, linkedItem) in resp.ModularContent)
            {
                // A component is invalidated through the item that owns it, so a key of its own is dead
                // weight. Its type still matters: the response does contain an item of that type.
                if (!ContentItemJsonHelper.IsComponent(linkedItem))
                {
                    dependencyContext.TrackItem(itemCodename);
                }

                dependencyContext.TrackItemType(ContentItemJsonHelper.ExtractContentType(linkedItem));
            }
        }

        if (!IsDynamicModel)
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

        return (item, [.. dependencyContext.Dependencies]);
    }

    private static IDeliveryResult<IContentItem<TModel>> CreateFailureResult(
        IDeliveryResult<DeliveryItemResponse<TModel>> deliveryResult) =>
        DeliveryResult.FailureFrom<IContentItem<TModel>, DeliveryItemResponse<TModel>>(deliveryResult);

    private string BuildCacheKey(CacheStorageMode storageMode)
    {
        var modelType = storageMode == CacheStorageMode.RawJson ? null : typeof(TModel);
        return CacheKeyBuilder.BuildItemKey(codename, _params, modelType);
    }

}
