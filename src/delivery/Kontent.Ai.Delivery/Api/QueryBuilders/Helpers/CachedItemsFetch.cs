using Kontent.Ai.Delivery.Caching;

namespace Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;

/// <summary>
/// The cached fetch the item and listing queries share. The two storage modes differ in what is stored -
/// the hydrated value, or a raw payload rehydrated on every hit - and in nothing else, so the fetch, the
/// failure handling and the provenance the manager reports are in one place.
/// </summary>
internal static class CachedItemsFetch
{
    /// <param name="cacheManager">The manager to read through.</param>
    /// <param name="cacheKey">The query's key.</param>
    /// <param name="expiration">The query's expiration override, if any.</param>
    /// <param name="fetch">Calls the API.</param>
    /// <param name="captureApiResult">Records what the factory fetched, for <see cref="CachedQueryExecutor"/>.</param>
    /// <param name="process">Hydrates a response and collects the dependencies it carries.</param>
    /// <param name="toPayload">The raw payload to store in <see cref="CacheStorageMode.RawJson"/> mode.</param>
    /// <param name="rehydrate">Rebuilds the value from a stored payload in <see cref="CacheStorageMode.RawJson"/> mode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    internal static Task<CacheResult<THydrated>?> ExecuteAsync<THydrated, TApi>(
        IDeliveryCacheManager cacheManager,
        string cacheKey,
        TimeSpan? expiration,
        Func<CancellationToken, Task<IDeliveryResult<TApi>>> fetch,
        Action<IDeliveryResult<TApi>> captureApiResult,
        Func<TApi, CancellationToken, Task<(THydrated Hydrated, string[] Dependencies)>> process,
        Func<THydrated, TApi, CachedRawItemsPayload> toPayload,
        Func<CachedRawItemsPayload, CancellationToken, Task<THydrated>> rehydrate,
        CancellationToken cancellationToken)
        where THydrated : class
        where TApi : class
        => cacheManager.StorageMode == CacheStorageMode.RawJson
            ? ExecuteRawAsync(cacheManager, cacheKey, expiration, fetch, captureApiResult, process, toPayload, rehydrate, cancellationToken)
            : cacheManager.GetOrSetAsync(
                cacheKey,
                async ct =>
                {
                    var fetched = await FetchAsync(fetch, captureApiResult, process, ct).ConfigureAwait(false);
                    return fetched is null ? null : new CacheEntry<THydrated>(fetched.Hydrated, fetched.Dependencies);
                },
                expiration,
                cancellationToken);

    private static async Task<CacheResult<THydrated>?> ExecuteRawAsync<THydrated, TApi>(
        IDeliveryCacheManager cacheManager,
        string cacheKey,
        TimeSpan? expiration,
        Func<CancellationToken, Task<IDeliveryResult<TApi>>> fetch,
        Action<IDeliveryResult<TApi>> captureApiResult,
        Func<TApi, CancellationToken, Task<(THydrated Hydrated, string[] Dependencies)>> process,
        Func<THydrated, TApi, CachedRawItemsPayload> toPayload,
        Func<CachedRawItemsPayload, CancellationToken, Task<THydrated>> rehydrate,
        CancellationToken cancellationToken)
        where THydrated : class
        where TApi : class
    {
        THydrated? hydratedHere = null;

        var cached = await cacheManager.GetOrSetAsync(
            cacheKey,
            async ct =>
            {
                var fetched = await FetchAsync(fetch, captureApiResult, process, ct).ConfigureAwait(false);
                if (fetched is null)
                    return null;

                hydratedHere = fetched.Hydrated;
                return new CacheEntry<CachedRawItemsPayload>(toPayload(fetched.Hydrated, fetched.Response), fetched.Dependencies);
            },
            expiration,
            cancellationToken).ConfigureAwait(false);

        if (cached is null)
            return null;

        // Hydrating a miss twice is what this avoids: the factory already built the value in order to
        // collect the dependency keys, and rehydrating parses and maps the very same payload again. Only
        // this call's own factory result can be reused - FromFactory is false for a cache hit and for a
        // background refresh, both of which still rehydrate.
        var value = cached.FromFactory && hydratedHere is not null
            ? hydratedHere
            : await rehydrate(cached.Value, cancellationToken).ConfigureAwait(false);

        // The rehydrated value replaces the stored payload; what the manager said about it carries across.
        return new CacheResult<THydrated>(value, cached.DependencyKeys)
        {
            FromFactory = cached.FromFactory,
            IsStale = cached.IsStale,
        };
    }

    private static async Task<Fetched<THydrated, TApi>?> FetchAsync<THydrated, TApi>(
        Func<CancellationToken, Task<IDeliveryResult<TApi>>> fetch,
        Action<IDeliveryResult<TApi>> captureApiResult,
        Func<TApi, CancellationToken, Task<(THydrated Hydrated, string[] Dependencies)>> process,
        CancellationToken cancellationToken)
        where THydrated : class
        where TApi : class
    {
        var result = await fetch(cancellationToken).ConfigureAwait(false);
        captureApiResult(result);
        if (!result.IsSuccess)
        {
            CachedQueryExecutor.ThrowIfOriginUnavailable(result);
            return null;
        }

        var (hydrated, dependencies) = await process(result.Value, cancellationToken).ConfigureAwait(false);
        return new Fetched<THydrated, TApi>(hydrated, dependencies, result.Value);
    }

    private sealed record Fetched<THydrated, TApi>(THydrated Hydrated, string[] Dependencies, TApi Response);
}
