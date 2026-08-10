using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.Webhooks;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<WebhookModel>>> ListWebhooksAsync(CancellationToken cancellationToken = default)
    {
        return ManagementApi.ListWebhooksInternalAsync(cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<WebhookModel>> GetWebhookAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetWebhookInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<WebhookModel>> CreateWebhookAsync(WebhookCreateModel webhook, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        return ManagementApi.CreateWebhookInternalAsync(webhook, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteWebhookAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteWebhookInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> EnableWebhookAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.EnableWebhookInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> DisableWebhookAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DisableWebhookInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }
}
