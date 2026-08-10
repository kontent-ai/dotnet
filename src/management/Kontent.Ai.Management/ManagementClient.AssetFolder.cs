using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.AssetFolders;
using Kontent.Ai.Management.Models.AssetFolders.Patch;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<AssetFoldersModel>> GetAssetFoldersAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.GetAssetFoldersInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<AssetFoldersModel>> CreateAssetFoldersAsync(AssetFolderCreateModel folders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);

        return ManagementApi.CreateAssetFoldersInternalAsync(folders, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<AssetFoldersModel>> ModifyAssetFoldersAsync(IEnumerable<AssetFolderOperationBaseModel> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ManagementApi.ModifyAssetFoldersInternalAsync(changes, cancellationToken).ToManagementResultAsync();
    }
}
