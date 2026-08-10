using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.Assets;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<AssetModel>>> ListAssetsAsync(CancellationToken cancellationToken = default)
        => PageEnumerator.CollectAsync<AssetListingResponseServerModel, AssetModel>(
            ManagementApi.ListAssetsInternalAsync,
            page => page.Assets,
            page => page.Pagination?.Token,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<IManagementResult<IReadOnlyList<AssetModel>>> EnumerateAssetPagesAsync(CancellationToken cancellationToken = default)
        => PageEnumerator.EnumerateAsync<AssetListingResponseServerModel, AssetModel>(
            ManagementApi.ListAssetsInternalAsync,
            page => page.Assets,
            page => page.Pagination?.Token,
            cancellationToken);

    /// <inheritdoc />
    public Task<IManagementResult<AssetModel>> GetAssetAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetAssetInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<AssetModel>> UpsertAssetAsync(Reference identifier, AssetUpsertModel asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(asset);

        return ManagementApi.UpsertAssetInternalAsync(identifier.ToUrlSegment(), asset, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<AssetModel>> CreateAssetAsync(AssetCreateModel asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return ManagementApi.CreateAssetInternalAsync(asset, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteAssetAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteAssetInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public async Task<IManagementResult<FileReference>> UploadFileAsync(FileContentSource fileContent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileContent);

        // Must stay async: `using` in a non-async Task-returning method disposes the content the instant the task is
        // returned, racing the send and defeating the retry-driven re-reads FileUploadContent exists for.
        using var content = new FileUploadContent(fileContent);
        return await ManagementApi.UploadFileInternalAsync(fileContent.FileName, content, cancellationToken).ToManagementResultAsync().ConfigureAwait(false);
    }
}
