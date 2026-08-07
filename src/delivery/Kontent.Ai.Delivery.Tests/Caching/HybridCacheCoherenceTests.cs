using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;

namespace Kontent.Ai.Delivery.Tests.Caching;

/// <summary>
/// Two managers over one distributed cache stand in for two nodes of the same application. What decides
/// whether an invalidation on one reaches the other is the backplane, not the memory tier: invalidation
/// state is held per instance.
/// </summary>
[CollectionDefinition(nameof(HybridCacheCoherenceTests), DisableParallelization = true)]
public class HybridCacheCoherenceCollection;

// Backplane notifications are asynchronous, so these do not share a machine with the rest of the suite.
[Collection(nameof(HybridCacheCoherenceTests))]
public class HybridCacheCoherenceTests
{
    private sealed record Payload
    {
        public string Value { get; init; } = "";
    }

    [Fact]
    public async Task Invalidation_ReachesTheNodeThatPerformedIt()
    {
        using var node = NewNode(NewDistributedCache(), backplane: null);

        await node.GetOrSetAsync("k", Factory("v1"));
        await node.InvalidateAsync(["dep"]);

        var served = await node.GetOrSetAsync("k", Factory("v2"));
        Assert.Equal("v2", served!.Value.Value);
    }

    [Fact]
    public async Task Invalidation_WithABackplane_ReachesTheOtherNode()
    {
        // A channel per test: MemoryBackplane connections are process-wide, so a shared id would let
        // notifications cross between tests.
        var channel = Guid.NewGuid().ToString("N");
        var shared = NewDistributedCache();
        using var nodeA = NewNode(shared, NewBackplane(channel));
        using var nodeB = NewNode(shared, NewBackplane(channel));

        await nodeA.GetOrSetAsync("k", Factory("v1"));
        await nodeB.GetOrSetAsync("k", Factory("v1"));

        await nodeA.InvalidateAsync(["dep"]);

        // The notification is asynchronous, so wait for it rather than for a fixed interval.
        var served = await EventuallyAsync(async () =>
            (await nodeB.GetOrSetAsync("k", Factory("v2")))!.Value.Value);

        Assert.Equal("v2", served);
    }

    [Fact]
    public async Task Invalidation_WithoutABackplane_DoesNotReachTheOtherNode()
    {
        // Pins why a backplane is required: invalidation state is per instance, so node B goes on serving
        // what node A evicted. Nothing about the memory tier changes this - it is bypassed here.
        var shared = NewDistributedCache();
        using var nodeA = NewNode(shared, backplane: null);
        using var nodeB = NewNode(shared, backplane: null);

        await nodeA.GetOrSetAsync("k", Factory("v1"));
        await nodeB.GetOrSetAsync("k", Factory("v1"));

        await nodeA.InvalidateAsync(["dep"]);

        var fromB = await nodeB.GetOrSetAsync("k", Factory("v2"));
        Assert.Equal("v1", fromB!.Value.Value);
    }

    private static HybridCacheManager NewNode(IDistributedCache cache, MemoryBackplane? backplane) =>
        new(cache, new DeliveryCacheOptions(), backplane: backplane);

    // Both nodes join one in-process channel, standing in for a shared Redis backplane.
    private static MemoryBackplane NewBackplane(string channel) =>
        new(Options.Create(new MemoryBackplaneOptions { ConnectionId = channel }));

    private static async Task<string> EventuallyAsync(Func<Task<string>> read)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string value;
        do
        {
            value = await read();
            if (value == "v2") return value;
            await Task.Delay(25);
        }
        while (DateTime.UtcNow < deadline);

        return value;
    }

    private static Func<CancellationToken, Task<CacheEntry<Payload>?>> Factory(string value) =>
        _ => Task.FromResult<CacheEntry<Payload>?>(
            new CacheEntry<Payload>(new Payload { Value = value }, ["dep"]));

    private static IDistributedCache NewDistributedCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }
}
