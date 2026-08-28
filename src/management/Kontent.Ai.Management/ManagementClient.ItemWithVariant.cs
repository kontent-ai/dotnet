using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.ItemWithVariant;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<ItemWithVariantFilterResultModel>>> ListItemsWithVariantsByFilterAsync(ItemWithVariantFilterRequestModel filterRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterRequest);

        return PageEnumerator.CollectAsync<ItemWithVariantFilterListingResponseServerModel, ItemWithVariantFilterResultModel>(
            (token, ct) => ManagementApi.FilterItemsWithVariantsInternalAsync(filterRequest, token, ct),
            page => page.Variants,
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<ItemWithVariantFilterResultModel>>> ListItemsWithVariantsByFilterPageAsync(ItemWithVariantFilterRequestModel filterRequest, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterRequest);

        return ManagementApi.FilterItemsWithVariantsInternalAsync(filterRequest, continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<ItemWithVariantFilterResultModel>
            {
                Items = page.Variants,
                ContinuationToken = page.Pagination?.Token,
            });
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<ContentItemWithVariantModel>>> ListItemsWithVariantsByBulkGetAsync(ItemWithVariantBulkGetRequestModel bulkGetRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bulkGetRequest);

        return PageEnumerator.CollectAsync<ContentItemsWithVariantsListingResponseServerModel, ContentItemWithVariantModel>(
            (token, ct) => ManagementApi.BulkGetItemsWithVariantsInternalAsync(bulkGetRequest, token, ct),
            page => page.Data,
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<ContentItemWithVariantModel>>> ListItemsWithVariantsByBulkGetPageAsync(ItemWithVariantBulkGetRequestModel bulkGetRequest, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bulkGetRequest);

        return ManagementApi.BulkGetItemsWithVariantsInternalAsync(bulkGetRequest, continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<ContentItemWithVariantModel>
            {
                Items = page.Data,
                ContinuationToken = page.Pagination?.Token,
            });
    }
}
