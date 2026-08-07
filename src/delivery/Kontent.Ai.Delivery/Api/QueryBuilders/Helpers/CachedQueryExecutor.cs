using Kontent.Ai.Delivery.Caching;

namespace Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;

/// <summary>
/// Where a cached query's value came from.
/// </summary>
internal enum CachedQuerySource
{
    /// <summary>This call's factory produced the value, so the API result it recorded is meaningful.</summary>
    Fetched,

    /// <summary>The cache served a stored value.</summary>
    CacheHit,

    /// <summary>The cache served a stale value because the origin could not be reached.</summary>
    FailSafeHit,
}

/// <summary>
/// The classified outcome of one cached query.
/// </summary>
/// <param name="Source">Where <paramref name="Cached"/> came from.</param>
/// <param name="Cached">The cached value, or <c>null</c> when nothing was cached or produced.</param>
/// <param name="ApiResult">
/// What the factory fetched. Only set when <paramref name="Source"/> is
/// <see cref="CachedQuerySource.Fetched"/> - on a cache hit the factory either did not run for this call
/// or ran in the background, and neither says anything about the value being returned.
/// </param>
internal readonly record struct CachedQueryOutcome<TCached, TApi>(
    CachedQuerySource Source,
    CacheResult<TCached>? Cached,
    IDeliveryResult<TApi>? ApiResult)
    where TCached : class
    where TApi : class;

/// <summary>
/// Runs a query through the cache and decides what came back.
/// </summary>
/// <remarks>
/// Nothing the factory records can be trusted here. With eager refresh enabled, FusionCache returns the
/// stale-but-valid value immediately and runs the factory on a background thread, so a captured local may
/// be written by a different call than the one reading it. The cache is the only component that knows
/// which value it handed back, so the decision comes from <see cref="CacheResult{T}.FromFactory"/> and,
/// for staleness, from the manager's fail-safe state.
/// </remarks>
internal static class CachedQueryExecutor
{
    /// <param name="cacheManager">The manager to read through.</param>
    /// <param name="cacheKey">The key being read, used to probe fail-safe state.</param>
    /// <param name="runCachedFetch">
    /// Performs the <c>GetOrSetAsync</c> call. Receives the callback the factory must use to record what
    /// it fetched; the storage-mode split and any post-processing stay with the caller.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    internal static async Task<CachedQueryOutcome<TCached, TApi>> ExecuteAsync<TCached, TApi>(
        IDeliveryCacheManager cacheManager,
        string cacheKey,
        Func<Action<IDeliveryResult<TApi>>, CancellationToken, Task<CacheResult<TCached>?>> runCachedFetch,
        CancellationToken cancellationToken)
        where TCached : class
        where TApi : class
    {
        IDeliveryResult<TApi>? apiResult = null;

        var cached = await runCachedFetch(result => apiResult = result, cancellationToken).ConfigureAwait(false);

        if (cached is not null && !cached.FromFactory)
        {
            var isFailSafe = cacheManager is IFailSafeStateProvider failSafeProvider
                && failSafeProvider.IsFailSafeActive(cacheKey);

            return new CachedQueryOutcome<TCached, TApi>(
                isFailSafe ? CachedQuerySource.FailSafeHit : CachedQuerySource.CacheHit,
                cached,
                ApiResult: null);
        }

        return new CachedQueryOutcome<TCached, TApi>(CachedQuerySource.Fetched, cached, apiResult);
    }
}
