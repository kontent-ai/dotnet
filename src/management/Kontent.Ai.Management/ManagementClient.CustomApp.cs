using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.CustomApps;
using Kontent.Ai.Management.Models.CustomApps.Patch;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<CustomAppModel>>> ListCustomAppsAsync(CancellationToken cancellationToken = default)
        => PageEnumerator.CollectAsync<CustomAppListingResponseServerModel, CustomAppModel>(
            ManagementApi.ListCustomAppsInternalAsync,
            page => page.CustomApps,
            page => page.Pagination?.Token,
            cancellationToken);

    /// <inheritdoc />
    public Task<IManagementResult<CustomAppModel>> GetCustomAppAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetCustomAppInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<CustomAppModel>> CreateCustomAppAsync(CustomAppCreateModel customApp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customApp);

        return ManagementApi.CreateCustomAppInternalAsync(customApp, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteCustomAppAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteCustomAppInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<CustomAppModel>> ModifyCustomAppAsync(Reference identifier, IEnumerable<CustomAppOperationBaseModel> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(changes);

        return ManagementApi.ModifyCustomAppInternalAsync(identifier.ToUrlSegment(), changes, cancellationToken).ToManagementResultAsync();
    }
}
