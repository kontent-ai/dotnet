using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Caching;

/// <summary>
/// Distributed implementation of <see cref="IDeliveryCacheManager"/> backed by FusionCache.
/// </summary>
/// <remarks>
/// FusionCache always has a memory tier in front of the distributed one; there is no distributed-only
/// mode, and this manager uses both tiers. The distributed one is what another node reads from, and a
/// backplane is what keeps the memory tiers in step: without one an invalidation reaches only the node
/// that performed it, so a second instance can go on serving content a webhook already evicted until the
/// entry expires. Multi-node deployments therefore need an <see cref="IFusionCacheBackplane"/>.
/// </remarks>
internal sealed class HybridCacheManager(
    IDistributedCache cache,
    DeliveryCacheOptions cacheOptions,
    JsonSerializerOptions? jsonSerializerOptions = null,
    ILogger<HybridCacheManager>? logger = null,
    IFusionCacheBackplane? backplane = null)
    : IDeliveryCacheManager, IDeliveryCachePurger, IFailSafeStateProvider, IDisposable
{
    private readonly FusionCacheManager _inner = FusionCacheManager.CreateHybrid(
        cache,
        cacheOptions,
        jsonSerializerOptions,
        logger,
        backplane);

    /// <inheritdoc />
    public CacheStorageMode StorageMode => _inner.StorageMode;

    /// <inheritdoc />
    public Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
        => _inner.GetOrSetAsync(cacheKey, factory, expiration, cancellationToken);

    /// <inheritdoc />
    public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
        => _inner.InvalidateAsync(dependencyKeys, cancellationToken);

    /// <inheritdoc />
    public Task PurgeAsync(bool allowFailSafe = false, CancellationToken cancellationToken = default)
        => _inner.PurgeAsync(allowFailSafe, cancellationToken);

    bool IFailSafeStateProvider.IsFailSafeActive(string cacheKey)
        => ((IFailSafeStateProvider)_inner).IsFailSafeActive(cacheKey);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
