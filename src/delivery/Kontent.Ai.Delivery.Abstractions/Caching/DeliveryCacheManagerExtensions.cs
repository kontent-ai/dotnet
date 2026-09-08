namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Invalidations that need a client to work out what to invalidate.
/// </summary>
public static class DeliveryCacheManagerExtensions
{
    /// <summary>
    /// Invalidates everything cached that depends on an asset: the responses whose rich text refers to it, by
    /// <see cref="DeliveryCacheDependencies.ForAsset"/>, and the items holding it in an asset element, which
    /// carry no asset id and are found through <see cref="IDeliveryClient.GetAssetUsedIn"/> and invalidated by
    /// <see cref="DeliveryCacheDependencies.ForItem"/>, together with <see cref="DeliveryCacheDependencies.ItemsListScope"/>
    /// since a listing may carry their asset element values.
    /// </summary>
    /// <param name="cacheManager">The cache manager of the client whose cache holds the responses.</param>
    /// <param name="client">The client that asks the API which items use the asset.</param>
    /// <param name="assetCodename">The asset's codename, as an asset webhook carries it.</param>
    /// <param name="assetId">The asset's id, as an asset webhook carries it.</param>
    /// <param name="cancellationToken">A token to cancel the lookup and the invalidation.</param>
    /// <returns>What <see cref="IDeliveryCacheManager.InvalidateAsync"/> returns for the collected keys.</returns>
    /// <exception cref="DeliveryRequestException">
    /// A page of the used-in lookup failed. Nothing has been invalidated; letting a webhook fail on it makes the
    /// platform deliver the event again.
    /// </exception>
    public static async Task<bool> InvalidateAssetAsync(
        this IDeliveryCacheManager cacheManager,
        IDeliveryClient client,
        string assetCodename,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cacheManager);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetCodename);

        var keys = new List<string> { DeliveryCacheDependencies.ForAsset(assetId) };

        await foreach (var usage in client.GetAssetUsedIn(assetCodename).EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(DeliveryCacheDependencies.ForItem(usage.System.Codename));
        }

        keys.Add(DeliveryCacheDependencies.ItemsListScope);

        return await cacheManager.InvalidateAsync([.. keys], cancellationToken).ConfigureAwait(false);
    }
}
