using Microsoft.Extensions.Caching.Distributed;

namespace Kontent.Ai.Delivery.Tests.Caching;

/// <summary>
/// A distributed cache that is down: every operation fails the way a lost Redis connection does.
/// </summary>
internal sealed class ThrowingDistributedCache : IDistributedCache
{
    private static IOException Down() => new("The distributed cache is unavailable.");

    public byte[]? Get(string key) => throw Down();
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
    public void Refresh(string key) => throw Down();
    public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
    public void Remove(string key) => throw Down();
    public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
}
