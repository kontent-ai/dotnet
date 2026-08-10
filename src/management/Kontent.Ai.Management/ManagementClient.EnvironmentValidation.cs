using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.EnvironmentReport;
using Kontent.Ai.Management.Models.EnvironmentValidation;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<EnvironmentReportModel>> ValidateEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.ValidateEnvironmentInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<AsyncValidationTaskModel>> InitiateEnvironmentAsyncValidationTaskAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.InitiateEnvironmentAsyncValidationTaskInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<AsyncValidationTaskModel>> GetAsyncValidationTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        return ManagementApi.GetAsyncValidationTaskInternalAsync(taskId, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<AsyncValidationTaskIssueModel>>> ListAsyncValidationTaskIssuesAsync(Guid taskId, CancellationToken cancellationToken = default)
        => PageEnumerator.CollectAsync<AsyncValidationTaskIssuesResponseServerModel, AsyncValidationTaskIssueModel>(
            (token, ct) => ManagementApi.ListAsyncValidationTaskIssuesInternalAsync(taskId, token, ct),
            page => page.Issues,
            page => page.Pagination?.Token,
            cancellationToken);
}
