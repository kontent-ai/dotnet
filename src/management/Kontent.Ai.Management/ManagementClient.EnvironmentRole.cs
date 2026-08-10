using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.Roles;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<EnvironmentRoleModel>>> ListEnvironmentRolesAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.ListEnvironmentRolesInternalAsync(cancellationToken).ToManagementResultAsync(static envelope => envelope.Roles);
    }

    /// <inheritdoc />
    public Task<IManagementResult<EnvironmentRoleModel>> GetEnvironmentRoleAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetEnvironmentRoleInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }
}
