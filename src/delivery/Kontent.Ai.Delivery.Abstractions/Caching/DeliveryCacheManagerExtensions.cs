namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Invalidations that need a client to work out what to invalidate.
/// </summary>
public static class DeliveryCacheManagerExtensions
{
    /// <summary>
    /// Invalidates rich-text asset dependencies, items using the asset across all languages, and the items-list scope.
    /// Language and usage lookups wait for fresh content and complete before any keys are invalidated.
    /// </summary>
    /// <param name="cacheManager">The cache manager of the client whose cache holds the responses.</param>
    /// <param name="client">The client that asks the API which items use the asset.</param>
    /// <param name="assetCodename">The asset's codename, as an asset webhook carries it.</param>
    /// <param name="assetId">The asset's id, as an asset webhook carries it.</param>
    /// <param name="cancellationToken">A token to cancel the lookup and the invalidation.</param>
    /// <returns>What <see cref="IDeliveryCacheManager.InvalidateAsync"/> returns for the collected keys.</returns>
    /// <exception cref="DeliveryRequestException">
    /// A language or used-in lookup failed before invalidation.
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

        var languages = await GetLanguageCodenamesAsync(client, cancellationToken).ConfigureAwait(false);
        var keys = new List<string> { DeliveryCacheDependencies.ForAsset(assetId) };
        var usages = client.GetAssetUsedIn(assetCodename)
            .Where(filter => filter.System("language").IsIn(languages))
            .WaitForLoadingNewContent();

        await foreach (var usage in usages.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(DeliveryCacheDependencies.ForItem(usage.System.Codename));
        }

        keys.Add(DeliveryCacheDependencies.ItemsListScope);

        return await cacheManager.InvalidateAsync(keys.Distinct(StringComparer.Ordinal).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string[]> GetLanguageCodenamesAsync(IDeliveryClient client, CancellationToken cancellationToken)
    {
        var languages = new List<string>();
        var page = await client.GetLanguages().WaitForLoadingNewContent().ExecuteAsync(cancellationToken).ConfigureAwait(false);

        while (page is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.IsSuccess)
            {
                throw new DeliveryRequestException(
                    page.Error?.Message ?? "The language lookup failed.", page.StatusCode, page.Error, page.RequestUrl);
            }

            languages.AddRange(page.Value.Languages.Select(language => language.System.Codename));
            page = await page.Value.FetchNextPageAsync(cancellationToken).ConfigureAwait(false);
        }

        if (languages.Count == 0)
        {
            throw new InvalidOperationException("The language lookup returned no languages.");
        }

        return languages.Distinct(StringComparer.Ordinal).ToArray();
    }
}
