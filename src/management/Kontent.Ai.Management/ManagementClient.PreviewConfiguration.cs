using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.PreviewConfiguration;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<PreviewConfigurationModel>> GetPreviewConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.GetPreviewConfigurationInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<PreviewConfigurationModel>> UpdatePreviewConfigurationAsync(PreviewConfigurationModel previewConfiguration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previewConfiguration);

        return ManagementApi.UpdatePreviewConfigurationInternalAsync(previewConfiguration, cancellationToken).ToManagementResultAsync();
    }
}
