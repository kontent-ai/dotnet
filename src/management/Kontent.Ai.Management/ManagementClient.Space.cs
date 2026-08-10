using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.Spaces;
using Kontent.Ai.Management.Models.Spaces.Patch;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<SpaceModel>> CreateSpaceAsync(SpaceCreateModel space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        return ManagementApi.CreateSpaceInternalAsync(space, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<SpaceModel>> GetSpaceAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetSpaceInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<SpaceModel>>> ListSpacesAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.ListSpacesInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<SpaceModel>> ModifySpaceAsync(Reference identifier, IEnumerable<SpaceReplacePatchModel> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(changes);

        return ManagementApi.ModifySpaceInternalAsync(identifier.ToUrlSegment(), changes, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteSpaceAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteSpaceInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }
}
