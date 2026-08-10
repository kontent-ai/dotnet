using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.LanguageVariants;
using Kontent.Ai.Management.Models.Publishing;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult> ChangeLanguageVariantWorkflowAsync(LanguageVariantIdentifier identifier, ChangeLanguageVariantWorkflowModel changeModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(changeModel);

        return ManagementApi.ChangeLanguageVariantWorkflowInternalAsync(identifier.ToUrlSegment(), changeModel, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> PublishLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.PublishLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> SchedulePublishingOfLanguageVariantAsync(LanguageVariantIdentifier identifier, ScheduleModel schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(schedule);

        return ManagementApi.SchedulePublishingOfLanguageVariantInternalAsync(identifier.ToUrlSegment(), schedule, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> SchedulePublishingAndUnpublishingOfLanguageVariantAsync(LanguageVariantIdentifier identifier, SchedulePublishAndUnpublishModel schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(schedule);

        return ManagementApi.SchedulePublishingAndUnpublishingOfLanguageVariantInternalAsync(identifier.ToUrlSegment(), schedule, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> CancelPublishingOfLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.CancelPublishingOfLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> UnpublishLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.UnpublishLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> CancelUnpublishingOfLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.CancelUnpublishingOfLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> ScheduleUnpublishingOfLanguageVariantAsync(LanguageVariantIdentifier identifier, ScheduleModel schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(schedule);

        return ManagementApi.ScheduleUnpublishingOfLanguageVariantInternalAsync(identifier.ToUrlSegment(), schedule, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult> CreateNewVersionOfLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.CreateNewVersionOfLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }
}
