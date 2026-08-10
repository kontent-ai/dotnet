using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.Environments;
using Kontent.Ai.Management.Models.Environments.Patch;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<EnvironmentClonedModel>> CloneEnvironmentAsync(EnvironmentCloneModel cloneEnvironmentModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloneEnvironmentModel);

        return ManagementApi.CloneEnvironmentInternalAsync(cloneEnvironmentModel, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<EnvironmentCloningStateModel>> GetEnvironmentCloningStateAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.GetEnvironmentCloningStateInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.DeleteEnvironmentInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> MarkEnvironmentAsProductionAsync(MarkAsProductionModel markAsProductionModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markAsProductionModel);

        return ManagementApi.MarkEnvironmentAsProductionInternalAsync(markAsProductionModel, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<EnvironmentModel>> ModifyEnvironmentAsync(IEnumerable<EnvironmentOperationBaseModel> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ManagementApi.ModifyEnvironmentInternalAsync(changes, cancellationToken).ToManagementResultAsync();
    }
}
