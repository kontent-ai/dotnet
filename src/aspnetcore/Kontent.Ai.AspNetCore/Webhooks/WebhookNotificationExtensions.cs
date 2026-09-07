using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery.Abstractions;

namespace Kontent.Ai.AspNetCore.Webhooks;

/// <summary>
/// Maps webhook notifications to the Delivery SDK's cache dependency keys, for <see cref="IDeliveryCacheManager.InvalidateAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The keys follow the format documented on <see cref="IDeliveryCacheManager"/>, so they are exactly the strings the
/// SDK tags cached responses with. A content item maps to its item key plus <see cref="DeliveryCacheDependencies.ItemsListScope"/>;
/// a content type to its type key plus <see cref="DeliveryCacheDependencies.TypesListScope"/>; a taxonomy to its group
/// key plus <see cref="DeliveryCacheDependencies.TaxonomiesListScope"/>; an asset to its asset key.
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

    private static string[] KeysFor(WebhookModel notification)
    {
        var system = notification.Data.System;
        return notification.Message.ObjectType switch
        {
            WebhookObjectTypes.ContentItem => [Key("item", system.Codename), DeliveryCacheDependencies.ItemsListScope],
            WebhookObjectTypes.ContentType => [Key("type", system.Codename), DeliveryCacheDependencies.TypesListScope],
            // For a term event the codename is the term's and the group is carried separately; for a group
            // event the codename is the group's. The cache is keyed by group either way.
            WebhookObjectTypes.Taxonomy => [Key("taxonomy", system.TaxonomyGroup ?? system.Codename), DeliveryCacheDependencies.TaxonomiesListScope],
            WebhookObjectTypes.Asset => [$"asset_{system.Id:D}"],
            _ => [],
        };
    }

    // The entity keys are composed here rather than through DeliveryCacheDependencies.ForItem/ForType/
    // ForTaxonomy/ForAsset because those arrive in a Delivery release the declared floor does not reach
    // yet. Same format and the same normalisation: codenames are lower-case by construction and the SDK
    // compares keys ordinally, so a differently-cased copy is normalised rather than silently matching nothing.
    private static string Key(string prefix, string codename) => $"{prefix}_{codename.Trim().ToLowerInvariant()}";
}
