using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <inheritdoc cref="IDynamicItemQuery"/>
/// <remarks>
/// This query performs runtime type resolution for dynamic items. Cache support is intentionally
/// omitted as the runtime-typed result type varies per item, making caching complex.
/// Use strongly-typed queries (<see cref="IItemQuery{TModel}"/>) for cacheable results.
/// </remarks>
internal sealed class DynamicItemQuery(
    IDeliveryApi api,
    string codename,
    ContentItemMapper contentItemMapper,
    ContentDeserializer contentDeserializer,
    string? defaultRenditionPreset = null,
    Uri? customAssetDomain = null,
    ILogger? logger = null) : IDynamicItemQuery
{
    private readonly ItemQuery<IDynamicElements> _inner = new(
        api,
        codename,
        contentItemMapper,
        contentDeserializer,
        cacheManager: null,
        defaultRenditionPreset,
        customAssetDomain,
        logger);

    public IDynamicItemQuery WithLanguage(string languageCodename)
    {
        _inner.WithLanguage(languageCodename);
        return this;
    }

    public IDynamicItemQuery WithElements(params string[] elementCodenames)
    {
        _inner.WithElements(elementCodenames);
        return this;
    }

    public IDynamicItemQuery WithoutElements(params string[] elementCodenames)
    {
        _inner.WithoutElements(elementCodenames);
        return this;
    }

    public IDynamicItemQuery Depth(int depth)
    {
        _inner.Depth(depth);
        return this;
    }

    public IDynamicItemQuery WaitForLoadingNewContent(bool enabled = true)
    {
        _inner.WaitForLoadingNewContent(enabled);
        return this;
    }

    public async Task<IDeliveryResult<DeliveryItemResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var (deliveryResult, modularContent) = await _inner.ExecuteUncachedAsync(cancellationToken).ConfigureAwait(false);

        if (!deliveryResult.IsSuccess)
        {
            return DeliveryResult.FailureFrom<DeliveryItemResponse, IContentItem<IDynamicElements>>(deliveryResult);
        }

        var dynamicItem = deliveryResult.Value;
        IContentItem item = dynamicItem;

        if (dynamicItem is IRawContentItem rawContentItem && rawContentItem.RawItemJson.HasValue)
        {
            item = await contentItemMapper.TryRuntimeTypeItemAsync(
                rawContentItem.RawItemJson.Value,
                modularContent,
                defaultRenditionPreset,
                customAssetDomain,
                cancellationToken).ConfigureAwait(false) ?? dynamicItem;
        }

        var response = new DeliveryItemResponse
        {
            Item = item,
            ModularContent = modularContent!
        };

        // Carried across explicitly: SuccessFrom projects the source's metadata but defaults the
        // dependency keys to null, and these are what output-cache tagging is documented to use.
        return DeliveryResult.SuccessFrom<DeliveryItemResponse, IContentItem<IDynamicElements>>(
            response, deliveryResult, deliveryResult.DependencyKeys);
    }
}
