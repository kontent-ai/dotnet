using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.Items;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<ContentItemModel>>> ListContentItemsAsync(CancellationToken cancellationToken = default)
        => PageEnumerator.CollectAsync<ContentItemListingResponseServerModel, ContentItemModel>(
            ManagementApi.ListContentItemsInternalAsync,
            page => page.Items,
            page => page.Pagination?.Token,
            cancellationToken);

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<ContentItemModel>>> ListContentItemsPageAsync(string? continuationToken = null, CancellationToken cancellationToken = default)
        => ManagementApi.ListContentItemsInternalAsync(continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<ContentItemModel>
            {
                Items = page.Items,
                ContinuationToken = page.Pagination?.Token,
            });

    /// <inheritdoc />
    public Task<IManagementResult<ContentItemModel>> GetContentItemAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetContentItemInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<ContentItemModel>> CreateContentItemAsync(ContentItemCreateModel contentItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentItem);

        return ManagementApi.CreateContentItemInternalAsync(contentItem, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<ContentItemModel>> UpsertContentItemAsync(Reference identifier, ContentItemUpsertModel contentItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(contentItem);

        return ManagementApi.UpsertContentItemInternalAsync(identifier.ToUrlSegment(), contentItem, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteContentItemAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteContentItemInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }
}
