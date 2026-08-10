using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.ContentModel.Patch;
using Kontent.Ai.Management.Models.TypeSnippets;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<ContentTypeSnippetModel>>> ListContentTypeSnippetsAsync(CancellationToken cancellationToken = default)
        => PageEnumerator.CollectAsync<SnippetListingResponseServerModel, ContentTypeSnippetModel>(
            ManagementApi.ListContentTypeSnippetsInternalAsync,
            page => page.Snippets,
            page => page.Pagination?.Token,
            cancellationToken);

    /// <inheritdoc />
    public Task<IManagementResult<ContentTypeSnippetModel>> GetContentTypeSnippetAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetContentTypeSnippetInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<ContentTypeSnippetModel>> CreateContentTypeSnippetAsync(ContentTypeSnippetCreateModel contentTypeSnippet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentTypeSnippet);

        return ManagementApi.CreateContentTypeSnippetInternalAsync(contentTypeSnippet, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteContentTypeSnippetAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteContentTypeSnippetInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<ContentTypeSnippetModel>> ModifyContentTypeSnippetAsync(Reference identifier, IEnumerable<ContentModelOperationBaseModel> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(changes);

        return ManagementApi.ModifyContentTypeSnippetInternalAsync(identifier.ToUrlSegment(), changes, cancellationToken).ToManagementResultAsync();
    }
}
