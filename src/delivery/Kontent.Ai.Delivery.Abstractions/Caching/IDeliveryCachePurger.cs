namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Optional capability interface for cache managers that can purge (invalidate) all entries at once.
/// </summary>
/// <remarks>
/// <para>
/// Not all cache backends can support purging all entries (e.g., generic <c>IDistributedCache</c>
/// does not provide key enumeration). This interface is intentionally separate from
/// <see cref="IDeliveryCacheManager"/> to avoid forcing unsupported operations.
/// </para>
/// <para>
/// Implementations should only purge entries managed by the specific cache manager instance
/// (including its configured key prefix/namespace).
/// </para>
/// </remarks>
public interface IDeliveryCachePurger
{
    /// <summary>
    /// Purges (invalidates) all cache entries managed by this cache manager.
    /// </summary>
    /// <param name="allowFailSafe">
    /// When <c>false</c> (default), entries are permanently removed.
    /// When <c>true</c>, entries are marked as logically expired but remain available as fail-safe fallbacks.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the purge completed through the configured cache operations;
    /// <see langword="false"/> when one of them failed or was skipped by an open circuit breaker.
    /// </returns>
    /// <remarks>
    /// Mirrors <see cref="IDeliveryCacheManager.InvalidateAsync"/>: operational failures are logged and reported
    /// through the result, not thrown. A <see langword="false"/> result can follow a partial purge - local entries
    /// may already be invalidated - so retry it. <see langword="true"/> does not acknowledge processing by every
    /// other node.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<bool> PurgeAsync(bool allowFailSafe = false, CancellationToken cancellationToken = default);
}
