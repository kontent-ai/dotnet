using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Caching;

/// <summary>
/// Distributed implementation of <see cref="IDeliveryCacheManager"/> backed by FusionCache.
/// </summary>
/// <remarks>
/// FusionCache always has a memory tier in front of the distributed one; there is no distributed-only
/// mode. This manager sets <c>SkipMemoryCacheRead</c> and <c>SkipMemoryCacheWrite</c> on its default
/// entry options to keep that tier out of the way, because no backplane is configured: without one an
/// invalidation reaches only the node that performed it, and a second instance would keep serving content
/// a webhook had already evicted. Coherence is chosen over the latency the memory tier would save.
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
