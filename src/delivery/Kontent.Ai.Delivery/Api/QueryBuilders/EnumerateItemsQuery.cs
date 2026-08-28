using Kontent.Ai.Delivery.Api.Filtering;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="IEnumerateItemsQuery{TModel}"/>
internal sealed class EnumerateItemsQuery<TModel>(
    IDeliveryApi api,
    ContentItemMapper contentItemMapper,
    ITypeProvider typeProvider,
    string? defaultRenditionPreset = null,
    Uri? customAssetDomain = null,
    ILogger? logger = null) : IEnumerateItemsQuery<TModel>
{
    private readonly QueryLoggingHelper _log = new(logger, "ItemsFeed", "feed");
    private EnumItemsParams _params = new();
    private bool _waitForLoadingNewContent;
    private readonly SerializedFilterCollection _serializedFilters = [];
    private bool _typeFilterApplied;
    private static bool IsDynamicModel => ModelTypeHelper.IsDynamic<TModel>();

    public IEnumerateItemsQuery<TModel> WithLanguage(string languageCodename, LanguageFallbackMode languageFallbackMode = LanguageFallbackMode.Enabled)
    {
        _params = _params with { Language = languageCodename };
        if (languageFallbackMode == LanguageFallbackMode.Disabled)
        {
            SystemFilterHelpers.AddSystemLanguageFilter(_serializedFilters, languageCodename);
        }
        return this;
    }

    public IEnumerateItemsQuery<TModel> WithElements(params string[] elementCodenames)
    {
        _params = _params with { Elements = string.Join(",", elementCodenames) };
        return this;
    }

    public IEnumerateItemsQuery<TModel> WithoutElements(params string[] elementCodenames)
    {
        _params = _params with { ExcludeElements = string.Join(",", elementCodenames) };
        return this;
    }

    public IEnumerateItemsQuery<TModel> OrderBy(string elementOrAttributePath, OrderingMode orderingMode = OrderingMode.Ascending)
    {
        _params = _params with
        {
            OrderBy = orderingMode == OrderingMode.Ascending
                ? $"{elementOrAttributePath}[asc]"
                : $"{elementOrAttributePath}[desc]"
        };
        return this;
    }

    public IEnumerateItemsQuery<TModel> WaitForLoadingNewContent(bool enabled = true)
    {
        _waitForLoadingNewContent = enabled;
        return this;
    }

    public IEnumerateItemsQuery<TModel> Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var filterBuilder = new ItemsFilterBuilder(_serializedFilters);
        build(filterBuilder);
        return this;
    }

    private void ApplyGenericTypeFilter()
    {
        if (_typeFilterApplied)
            return;
        _typeFilterApplied = true;

        SystemFilterHelpers.AddGenericTypeFilter<TModel>(_serializedFilters, typeProvider, logger);
    }

    public Task<IDeliveryResult<IDeliveryItemsFeedResponse<TModel>>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        ExecuteLoggedAsync(continuationToken: null, cancellationToken);

    public Task<IDeliveryResult<IDeliveryItemsFeedResponse<TModel>>> ExecuteAsync(string continuationToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(continuationToken);

        return ExecuteLoggedAsync(continuationToken, cancellationToken);
    }

    // Wraps one request in the same starting/completed bracket every other query builder emits. A walk goes straight
    // to ExecutePageAsync instead: it has its own Pagination bracket, and per-page timings would drown it.
    private async Task<IDeliveryResult<IDeliveryItemsFeedResponse<TModel>>> ExecuteLoggedAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        _log.LogQueryStarting();
        var stopwatch = _log.StartTimingIfEnabled();

        var result = await ExecutePageAsync(continuationToken, cancellationToken).ConfigureAwait(false);

        _log.LogQueryCompleted(stopwatch, result.StatusCode, result.IsCacheHit, result.HasStaleContent);
        return result;
    }

    // Takes the token as an argument rather than reading the field, so a walk never mutates the query it came from.
    // Internal, not private: the dynamic wrapper's walk needs this unlogged path too.
    internal async Task<IDeliveryResult<IDeliveryItemsFeedResponse<TModel>>> ExecutePageAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        ApplyGenericTypeFilter();

        bool? waitForLoadingNewContent = _waitForLoadingNewContent ? true : null;
        var (deliveryResult, nextContinuationToken) = await FetchFeedPageAsync(
            continuationToken,
            waitForLoadingNewContent,
            cancellationToken).ConfigureAwait(false);

        if (!deliveryResult.IsSuccess)
        {
            _log.LogQueryFailed(deliveryResult.StatusCode, deliveryResult.Error?.Message);
            return CreateFailureResult(deliveryResult);
        }

        var response = await PreparePageAsync(deliveryResult.Value, nextContinuationToken, cancellationToken).ConfigureAwait(false);
        return WrapSuccess(response, deliveryResult);
    }

    // The logged path: FetchNextPageAsync is one caller-issued request, exactly like ExecuteAsync(token), and gets
    // the same bracket. Only the walk skips it, because it brackets itself once with Pagination*.
    private Func<CancellationToken, Task<IDeliveryResult<IDeliveryItemsFeedResponse<TModel>>>>? CreateNextPageFetcher(string? continuationToken) =>
        string.IsNullOrEmpty(continuationToken) ? null : (ct => ExecuteLoggedAsync(continuationToken, ct));

    public DeliveryEnumeration<IContentItem<TModel>> EnumerateAsync(CancellationToken cancellationToken = default) =>
        new LoggedDeliveryEnumeration<IContentItem<TModel>, IDeliveryItemsFeedResponse<TModel>>(
            "ItemsFeed",
            logger,
            ExecutePageAsync,
            static response => new DeliveryPage<IContentItem<TModel>>
            {
                Items = response.Items,
                ContinuationToken = response.ContinuationToken,
            },
            cancellationToken);

    private async Task<(IDeliveryResult<DeliveryItemsFeedResponse<TModel>> DeliveryResult, string? ContinuationToken)> FetchFeedPageAsync(
        string? continuationToken,
        bool? waitForLoadingNewContent,
        CancellationToken cancellationToken)
    {
        var resp = await api
            .GetItemsFeedInternalAsync<TModel>(
                _params,
                FilterQueryString.Render(_serializedFilters),
                continuationToken,
                waitForLoadingNewContent,
                cancellationToken)
            .ConfigureAwait(false);

        var deliveryResult = await resp.ToDeliveryResultAsync(logger).ConfigureAwait(false);
        return (deliveryResult, resp.Continuation());
    }

    private async Task<DeliveryItemsFeedResponse<TModel>> PreparePageAsync(
        DeliveryItemsFeedResponse<TModel> content,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        if (!IsDynamicModel)
        {
            foreach (var item in content.Items)
            {
                await contentItemMapper.CompleteItemAsync(
                        item,
                        content.ModularContent,
                        dependencyContext: null,
                        defaultRenditionPreset,
                        customAssetDomain,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return content with
        {
            ContinuationToken = continuationToken,
            NextPageFetcher = CreateNextPageFetcher(continuationToken)
        };
    }

    private static IDeliveryResult<IDeliveryItemsFeedResponse<TModel>> WrapSuccess(
        DeliveryItemsFeedResponse<TModel> response,
        IDeliveryResult<DeliveryItemsFeedResponse<TModel>> apiResult) =>
        DeliveryResult.SuccessFrom<IDeliveryItemsFeedResponse<TModel>, DeliveryItemsFeedResponse<TModel>>(response, apiResult);

    private static IDeliveryResult<IDeliveryItemsFeedResponse<TModel>> CreateFailureResult(
        IDeliveryResult<DeliveryItemsFeedResponse<TModel>> deliveryResult) =>
        DeliveryResult.FailureFrom<IDeliveryItemsFeedResponse<TModel>, DeliveryItemsFeedResponse<TModel>>(deliveryResult);
}
