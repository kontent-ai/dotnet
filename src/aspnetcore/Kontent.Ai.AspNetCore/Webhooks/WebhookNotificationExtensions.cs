using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery.Abstractions;

namespace Kontent.Ai.AspNetCore.Webhooks;

/// <summary>
/// Turns webhook notifications into Delivery SDK cache invalidations.
/// </summary>
/// <remarks>
/// <para>
/// The keys are composed with <see cref="DeliveryCacheDependencies"/>, so they are exactly the strings the SDK tags
/// cached responses with. A content item maps to its item key plus <see cref="DeliveryCacheDependencies.ItemsListScope"/>;
/// a content type to its type key plus <see cref="DeliveryCacheDependencies.TypesListScope"/>; a taxonomy to its group
/// key plus <see cref="DeliveryCacheDependencies.TaxonomiesListScope"/>; an asset to its asset key.
/// Type and taxonomy events also invalidate <see cref="DeliveryCacheDependencies.ItemsListScope"/> because they can
/// change membership of empty or projected item listings that carry no matching detail key.
/// </para>
/// <para>
/// The asset key reaches rich-text usages only. An asset element carries no asset id, so the items holding the asset
/// that way are found through the SDK's used-in lookup, which needs a client:
/// <see cref="InvalidateAsync(IDeliveryCacheManager, WebhookNotification, IDeliveryClient, CancellationToken)"/> does both.
/// </para>
/// <para>
/// A language notification maps to nothing: the SDK keeps no language dependency, and a language change can affect
/// any cached response through fallbacks. Purge the cache for those (<see cref="IDeliveryCachePurger"/>). A rename
/// carries only the new codename, so a response cached under the old key lives until it expires.
/// </para>
/// </remarks>
public static class WebhookNotificationExtensions
{
    /// <summary>
    /// The cache dependency keys affected by every notification in the batch, without duplicates.
    /// </summary>
    public static string[] GetCacheDependencyKeys(this WebhookNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification.Notifications.GetCacheDependencyKeys();
    }

    /// <summary>
    /// The cache dependency keys affected by the given notifications, without duplicates.
    /// </summary>
    public static string[] GetCacheDependencyKeys(this IEnumerable<WebhookModel> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        return [.. notifications.SelectMany(KeysFor).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Invalidates the batch's supported dependency keys and, for an asset notification, the items using the asset, which
    /// <see cref="DeliveryCacheManagerExtensions.InvalidateAssetAsync"/> resolves through <paramref name="client"/>.
    /// </summary>
    /// <remarks>
    /// Language events require a separate purge. Renames cannot invalidate detail keys using the old codename.
    /// Environment and delivery-slot filtering are the caller's responsibility.
    /// </remarks>
    /// <returns><c>false</c> when any invalidation did not complete, so the webhook can be answered with a status Kontent.ai retries.</returns>
    /// <exception cref="DeliveryRequestException">An asset's usage lookup failed; nothing was invalidated for that asset.</exception>
    public static Task<bool> InvalidateAsync(
        this IDeliveryCacheManager cacheManager,
        WebhookNotification notification,
        IDeliveryClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return cacheManager.InvalidateAsync(notification.Notifications, client, cancellationToken);
    }

    /// <inheritdoc cref="InvalidateAsync(IDeliveryCacheManager, WebhookNotification, IDeliveryClient, CancellationToken)"/>
    public static async Task<bool> InvalidateAsync(
        this IDeliveryCacheManager cacheManager,
        IEnumerable<WebhookModel> notifications,
        IDeliveryClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cacheManager);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(client);

        var batch = notifications.ToList();
        var keys = batch.Where(n => n.Message.ObjectType != WebhookObjectTypes.Asset).GetCacheDependencyKeys();
        var invalidated = keys.Length == 0 || await cacheManager.InvalidateAsync(keys, cancellationToken);

        var assets = batch
            .Where(n => n.Message.ObjectType == WebhookObjectTypes.Asset)
            .Select(n => n.Data.System)
            .DistinctBy(asset => asset.Id);
        foreach (var asset in assets)
        {
            invalidated &= await cacheManager.InvalidateAssetAsync(client, asset.Codename, asset.Id, cancellationToken);
        }

        return invalidated;
    }

    private static string[] KeysFor(WebhookModel notification)
    {
        var system = notification.Data.System;
        return notification.Message.ObjectType switch
        {
            WebhookObjectTypes.ContentItem => [DeliveryCacheDependencies.ForItem(system.Codename), DeliveryCacheDependencies.ItemsListScope],
            WebhookObjectTypes.ContentType => [DeliveryCacheDependencies.ForType(system.Codename), DeliveryCacheDependencies.TypesListScope, DeliveryCacheDependencies.ItemsListScope],
            // For a term event the codename is the term's and the group is carried separately; for a group
            // event the codename is the group's. The cache is keyed by group either way.
            WebhookObjectTypes.Taxonomy => [DeliveryCacheDependencies.ForTaxonomy(system.TaxonomyGroup ?? system.Codename), DeliveryCacheDependencies.TaxonomiesListScope, DeliveryCacheDependencies.ItemsListScope],
            WebhookObjectTypes.Asset => [DeliveryCacheDependencies.ForAsset(system.Id)],
            _ => [],
        };
    }
}
