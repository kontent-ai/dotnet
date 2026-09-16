using System.Text.Json;
using AwesomeAssertions;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;

namespace Kontent.Ai.Delivery.Tests.Caching;

/// <summary>
/// Pins what a custom cache manager persists in <see cref="CacheStorageMode.RawJson"/>, and the key
/// version that retires it. The payload is an SDK-internal shape, so a change here is invisible to
/// consumers until their store starts returning entries the new SDK cannot read.
/// </summary>
public class CachePayloadFormatTests
{
    [Fact]
    public void RawJsonPayload_SerializesToTheShapeStoresPersist()
    {
        var payload = new CachedRawItemsPayload
        {
            ItemsJson = ["{\"system\":{\"codename\":\"hero\"}}"],
            ModularContentJson = new Dictionary<string, string> { ["linked"] = "{\"system\":{}}" },
            Pagination = null
        };

        var json = JsonSerializer.Serialize(payload);

        // Changing this string changes what every custom RawJson manager has already written to its
        // store. Bump FusionCacheManager.DistributedFormatVersion with it so the built-in manager
        // stops reading old entries, and say so in the changelog.
        json.Should().Be(
            """{"ItemsJson":["{\u0022system\u0022:{\u0022codename\u0022:\u0022hero\u0022}}"],"ModularContentJson":{"linked":"{\u0022system\u0022:{}}"},"Pagination":null}""");
    }

    [Fact]
    public async Task HybridManager_VersionsTheKeysItWritesToTheDistributedStore()
    {
        var store = new KeyRecordingDistributedCache();
        using var manager = FusionCacheManager.CreateHybrid(store, new DeliveryCacheOptions { KeyPrefix = "prefix" });

        await manager.GetOrSetAsync("item|hero", _ => Task.FromResult<CacheEntry<string>?>(new("value", ["item_hero"])));

        store.Keys.Should().NotBeEmpty();
        // Bumping DistributedFormatVersion retires every entry a previous format wrote.
        store.Keys.Should().AllSatisfy(key => key.Should().StartWith("prefix:v1:"));
    }

    private sealed class KeyRecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _entries = [];

        public IReadOnlyCollection<string> Keys
        {
            get { lock (_entries) { return [.. _entries.Keys]; } }
        }

        public byte[]? Get(string key)
        {
            lock (_entries) { return _entries.TryGetValue(key, out var value) ? value : null; }
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            lock (_entries) { _entries[key] = value; }
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { lock (_entries) { _entries.Remove(key); } }
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
    }
}
