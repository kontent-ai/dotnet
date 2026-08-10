using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.ContentModel.Patch;
using Kontent.Ai.Management.Models.Types;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<ContentTypeModel>>> ListContentTypesAsync(CancellationToken cancellationToken = default)
        => PageEnumerator.CollectAsync<ContentTypeListingResponseServerModel, ContentTypeModel>(
            ManagementApi.ListContentTypesInternalAsync,
            page => page.Types,
            page => page.Pagination?.Token,
            cancellationToken);

    /// <inheritdoc />
    public Task<IManagementResult<ContentTypeModel>> GetContentTypeAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetContentTypeInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<ContentTypeModel>> CreateContentTypeAsync(ContentTypeCreateModel contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        return ManagementApi.CreateContentTypeInternalAsync(contentType, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteContentTypeAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteContentTypeInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<ContentTypeModel>> ModifyContentTypeAsync(Reference identifier, IEnumerable<ContentModelOperationBaseModel> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(changes);

        return ManagementApi.ModifyContentTypeInternalAsync(identifier.ToUrlSegment(), changes, cancellationToken).ToManagementResultAsync();
    }
}
