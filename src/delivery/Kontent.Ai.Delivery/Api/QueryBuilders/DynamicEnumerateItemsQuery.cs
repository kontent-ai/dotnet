using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="IDynamicEnumerateItemsQuery"/>
internal sealed class DynamicEnumerateItemsQuery(
    IDeliveryApi api,
    ContentItemMapper contentItemMapper,
    ITypeProvider typeProvider,
    string? defaultRenditionPreset = null,
    Uri? customAssetDomain = null,
    ILogger? logger = null) : IDynamicEnumerateItemsQuery
{
    private readonly EnumerateItemsQuery<IDynamicElements> _inner = new(
        api,
        contentItemMapper,
        typeProvider,
        defaultRenditionPreset,
        customAssetDomain,
        logger);

    public IDynamicEnumerateItemsQuery WithLanguage(string languageCodename, LanguageFallbackMode languageFallbackMode = LanguageFallbackMode.Enabled)
    {
        _inner.WithLanguage(languageCodename, languageFallbackMode);
        return this;
    }

    public IDynamicEnumerateItemsQuery WithElements(params string[] elementCodenames)
    {
        _inner.WithElements(elementCodenames);
        return this;
    }

    public IDynamicEnumerateItemsQuery WithoutElements(params string[] elementCodenames)
    {
        _inner.WithoutElements(elementCodenames);
        return this;
    }

    public IDynamicEnumerateItemsQuery OrderBy(string elementOrAttributePath, OrderingMode orderingMode = OrderingMode.Ascending)
    {
        _inner.OrderBy(elementOrAttributePath, orderingMode);
        return this;
    }

    public IDynamicEnumerateItemsQuery WaitForLoadingNewContent(bool enabled = true)
    {
        _inner.WaitForLoadingNewContent(enabled);
        return this;
    }

    public IDynamicEnumerateItemsQuery Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build)
    {
        _inner.Where(build);
        return this;
    }

    public async Task<IDeliveryResult<IDeliveryItemsFeedResponse>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await ConvertResultAsync(await _inner.ExecuteAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    public async Task<IDeliveryResult<IDeliveryItemsFeedResponse>> ExecuteAsync(string continuationToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(continuationToken);

        return await ConvertResultAsync(await _inner.ExecuteAsync(continuationToken, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    // The walk's per-page fetch. Goes to the inner query's unlogged path: a walk is bracketed once by Pagination*,
    // not once per page by the query-starting/completed pair the public overloads above emit.
    private async Task<IDeliveryResult<IDeliveryItemsFeedResponse>> ExecutePageAsync(string? continuationToken, CancellationToken cancellationToken) =>
        await ConvertResultAsync(await _inner.ExecutePageAsync(continuationToken, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    private async Task<IDeliveryResult<IDeliveryItemsFeedResponse>> ConvertResultAsync(
        IDeliveryResult<IDeliveryItemsFeedResponse<IDynamicElements>> deliveryResult,
        CancellationToken cancellationToken)
    {
        if (!deliveryResult.IsSuccess)
        {
            return DeliveryResult.FailureFrom<IDeliveryItemsFeedResponse, IDeliveryItemsFeedResponse<IDynamicElements>>(deliveryResult);
        }

        var nextContinuationToken = deliveryResult.Value.ContinuationToken;
        var response = await ConvertResponseAsync(deliveryResult.Value, nextContinuationToken, cancellationToken).ConfigureAwait(false);

        return DeliveryResult.SuccessFrom<IDeliveryItemsFeedResponse, IDeliveryItemsFeedResponse<IDynamicElements>>(response, deliveryResult);
    }

    private async Task<DynamicDeliveryItemsFeedResponse> ConvertResponseAsync(
        IDeliveryItemsFeedResponse<IDynamicElements> response,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var runtimeTypedItems = await contentItemMapper.RuntimeTypeItemsAsync(
            response.Items,
            response.ModularContent,
            defaultRenditionPreset,
            customAssetDomain,
            cancellationToken).ConfigureAwait(false);

        return new DynamicDeliveryItemsFeedResponse
        {
            Items = runtimeTypedItems,
            ModularContent = response.ModularContent,
            ContinuationToken = continuationToken,
            NextPageFetcher = CreateNextPageFetcher(response)
        };
    }

    private Func<CancellationToken, Task<IDeliveryResult<IDeliveryItemsFeedResponse>>>? CreateNextPageFetcher(
        IDeliveryItemsFeedResponse<IDynamicElements> page) => !page.HasNextPage ? null : (ct => FetchNextPageAndConvertAsync(page, ct));

    private async Task<IDeliveryResult<IDeliveryItemsFeedResponse>> FetchNextPageAndConvertAsync(
        IDeliveryItemsFeedResponse<IDynamicElements> currentPage,
        CancellationToken cancellationToken)
    {
        var nextPageResult = await currentPage.FetchNextPageAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The current feed page indicated a next page, but fetching it returned null.");

        return await ConvertResultAsync(nextPageResult, cancellationToken).ConfigureAwait(false);
    }

    public DeliveryEnumeration<IContentItem> EnumerateAsync(CancellationToken cancellationToken = default) =>
        LoggedDeliveryEnumeration.Create<IContentItem, IDeliveryItemsFeedResponse>(
            "ItemsFeed",
            logger,
            ExecutePageAsync,
            static response => new DeliveryPage<IContentItem>
            {
                Items = response.Items,
                ContinuationToken = response.ContinuationToken,
            },
            cancellationToken);
}
