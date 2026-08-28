using Kontent.Ai.Delivery.Api.Filtering;
using Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;
using Kontent.Ai.Delivery.UsedIn;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="IItemUsedInQuery"/>
internal sealed class ItemUsedInQuery(
    IDeliveryApi api,
    string codename,
    ILogger? logger = null) : IItemUsedInQuery
{
    private readonly SerializedFilterCollection _serializedFilters = [];
    private readonly UsedInQueryCore _core = new(
        "ItemUsedIn",
        codename,
        api.GetItemUsedInInternalAsync,
        logger);

    public IItemUsedInQuery WaitForLoadingNewContent(bool enabled = true)
    {
        _core.WaitForLoadingNewContent(enabled);
        return this;
    }

    public IItemUsedInQuery Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var filterBuilder = new ItemsFilterBuilder(_serializedFilters);
        build(filterBuilder);
        return this;
    }

    public Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(CancellationToken cancellationToken = default)
        => _core.ExecuteLoggedAsync(_serializedFilters, continuationToken: null, cancellationToken);

    public Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(string continuationToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(continuationToken);

        return _core.ExecuteLoggedAsync(_serializedFilters, continuationToken, cancellationToken);
    }

    public DeliveryEnumeration<IUsedInItem> EnumerateAsync(CancellationToken cancellationToken = default)
        => _core.CreateEnumeration(_serializedFilters, cancellationToken);
}

/// <inheritdoc cref="IAssetUsedInQuery"/>
internal sealed class AssetUsedInQuery(
    IDeliveryApi api,
    string codename,
    ILogger? logger = null) : IAssetUsedInQuery
{
    private readonly SerializedFilterCollection _serializedFilters = [];
    private readonly UsedInQueryCore _core = new(
        "AssetUsedIn",
        codename,
        api.GetAssetUsedInInternalAsync,
        logger);

    public IAssetUsedInQuery WaitForLoadingNewContent(bool enabled = true)
    {
        _core.WaitForLoadingNewContent(enabled);
        return this;
    }

    public IAssetUsedInQuery Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var filterBuilder = new ItemsFilterBuilder(_serializedFilters);
        build(filterBuilder);
        return this;
    }

    public Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(CancellationToken cancellationToken = default)
        => _core.ExecuteLoggedAsync(_serializedFilters, continuationToken: null, cancellationToken);

    public Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(string continuationToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(continuationToken);

        return _core.ExecuteLoggedAsync(_serializedFilters, continuationToken, cancellationToken);
    }

    public DeliveryEnumeration<IUsedInItem> EnumerateAsync(CancellationToken cancellationToken = default)
        => _core.CreateEnumeration(_serializedFilters, cancellationToken);
}


internal sealed class UsedInQueryCore(
    string queryType,
    string codename,
    Func<string, string?, bool?, string?, CancellationToken, Task<IApiResponse<DeliveryUsedInResponse>>> fetchPage,
    ILogger? logger)
{
    private readonly QueryLoggingHelper _log = new(logger, queryType, codename);
    private bool _waitForLoadingNewContent;

    public void WaitForLoadingNewContent(bool enabled = true) => _waitForLoadingNewContent = enabled;

    // Wraps one request in the same starting/completed bracket every other query builder emits. A walk goes straight
    // to ExecutePageAsync instead: it has its own Pagination bracket, and per-page timings would drown it.
    public async Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteLoggedAsync(
        SerializedFilterCollection filters,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        _log.LogQueryStarting();
        var stopwatch = _log.StartTimingIfEnabled();

        var result = await ExecutePageAsync(filters, continuationToken, cancellationToken).ConfigureAwait(false);

        _log.LogQueryCompleted(stopwatch, result.StatusCode, result.IsCacheHit, result.HasStaleContent);
        return result;
    }

    public DeliveryEnumeration<IUsedInItem> CreateEnumeration(SerializedFilterCollection filters, CancellationToken cancellationToken) =>
        new LoggedDeliveryEnumeration<IUsedInItem, DeliveryPage<IUsedInItem>>(
            queryType,
            logger,
            (token, ct) => ExecutePageAsync(filters, token, ct),
            static page => page,
            cancellationToken);

    public async Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecutePageAsync(
        SerializedFilterCollection filters,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        bool? waitForLoadingNewContent = _waitForLoadingNewContent ? true : null;

        var response = await fetchPage(
            codename,
            FilterQueryString.Render(filters),
            waitForLoadingNewContent,
            continuationToken,
            cancellationToken).ConfigureAwait(false);

        var deliveryResult = await response.ToDeliveryResultAsync(logger).ConfigureAwait(false);
        if (!deliveryResult.IsSuccess)
        {
            _log.LogQueryFailed(deliveryResult.StatusCode, deliveryResult.Error?.Message);
            return DeliveryResult.FailureFrom<DeliveryPage<IUsedInItem>, DeliveryUsedInResponse>(deliveryResult);
        }

        var page = new DeliveryPage<IUsedInItem>
        {
            Items = deliveryResult.Value.Items,
            ContinuationToken = response.Continuation(),
        };

        return DeliveryResult.SuccessFrom(page, deliveryResult);
    }
}
