using System.Text.Json;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Kontent.Ai.Delivery.Caching;

/// <summary>
/// The SDK's cache manager over FusionCache, in one of two shapes: <see cref="CreateMemory"/> caches
/// hydrated objects in the application's memory cache, <see cref="CreateHybrid"/> caches raw payloads in
/// a memory tier of its own in front of a distributed cache.
/// </summary>
internal sealed class FusionCacheManager : IDeliveryCacheManager, IDeliveryCachePurger, IDisposable
{
    private readonly IFusionCache _cache;
    private readonly CacheStorageMode _storageMode;
    private readonly TimeSpan _defaultExpiration;
    private readonly string _keyPrefix;
    private readonly ILogger? _logger;
    private readonly FusionCacheEntryOptions _baseWriteOptions;

    /// <summary>
    /// What every entry weighs under a <see cref="MemoryCacheOptions.SizeLimit"/>. The application's
    /// memory cache may have one, and a cache with a limit refuses any entry that declares no size - so
    /// every entry the SDK writes, tag-expiration entries included, declares this one.
    /// </summary>
    private const long EntrySize = 1;

    /// <summary>
    /// The shape of what the distributed tier stores, as a key segment. A Redis outlives a deployment, so
    /// an entry written by the previous version of the SDK is read by the next one; a key that names the
    /// shape lets that read miss instead of failing to deserialize. Bump it whenever a cached type -
    /// <see cref="CacheEnvelope{T}"/>, <c>CachedRawItemsPayload</c>, a wire model - or FusionCache's own
    /// distributed entry format changes.
    /// </summary>
    private const string DistributedFormatVersion = "v1:";

    /// <summary>
    /// The <see cref="GetOrSetAsync{T}"/> call in flight on the current async context. FusionCache raises
    /// its events inline (<see cref="FusionCacheOptions.EnableSyncEventHandlersExecution"/>), so a stale
    /// hit or a fail-safe activation it reports while a call awaits it belongs to that call, and the
    /// handlers record it there. Nothing outlives the call: no shared state, nothing to cap or evict.
    /// </summary>
    private static readonly AsyncLocal<StaleObservation?> CurrentCall = new();

    private readonly EventHandler<FusionCacheEntryEventArgs> _failSafeActivateHandler;
    private readonly EventHandler<FusionCacheEntryHitEventArgs> _hitHandler;
    private int _disposeState;

    private FusionCacheManager(
        IFusionCache cache,
        CacheStorageMode storageMode,
        TimeSpan defaultExpiration,
        string keyPrefix,
        ILogger? logger,
        FusionCacheEntryOptions baseWriteOptions)
    {
        _cache = cache;
        _storageMode = storageMode;
        _defaultExpiration = defaultExpiration;
        _keyPrefix = keyPrefix;
        _logger = logger;
        _baseWriteOptions = baseWriteOptions;

        _failSafeActivateHandler = HandleFailSafeActivate;
        _hitHandler = HandleHit;
        _cache.Events.FailSafeActivate += _failSafeActivateHandler;
        _cache.Events.Hit += _hitHandler;
    }

    /// <summary>
    /// Builds the segment this client's cache lives under. It goes to FusionCache as its
    /// <see cref="FusionCacheOptions.CacheKeyPrefix"/>, so every key FusionCache stores carries it - the
    /// entries, the tag data an invalidation writes, and the marker a purge writes - and two clients
    /// sharing one store cannot reach each other's, purges included.
    /// </summary>
    /// <remarks>
    /// The environment id is part of it because a cache store can outlive the process and be shared: two
    /// applications pointing at different environments and sharing one Redis would otherwise compute the
    /// same key for "the item <c>article</c>" and serve each other's content. <c>KeyPrefix</c> stays in
    /// front of it, so an explicit prefix still separates clients within one environment.
    /// </remarks>
    private static string ComposeKeyPrefix(string? keyPrefix, string? environmentId)
    {
        var parts = new[] { keyPrefix, environmentId }.Where(part => !string.IsNullOrEmpty(part));

        return parts.Any() ? $"{string.Join(':', parts)}:" : string.Empty;
    }

    /// <summary>
    /// Names the FusionCache instance, and through it the backplane channel: clients on different
    /// environments must not share one, or every node hears every other environment's notifications.
    /// </summary>
    private static string ComposeCacheName(string tier, string keyPrefix)
        => $"KontentDelivery.{tier}.{(keyPrefix.Length == 0 ? "Default" : keyPrefix.TrimEnd(':'))}";

    public static FusionCacheManager CreateMemory(
        IMemoryCache memoryCache,
        DeliveryCacheOptions cacheOptions,
        ILogger? logger = null,
        string? environmentId = null,
        ILogger<FusionCache>? fusionCacheLogger = null)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        ArgumentNullException.ThrowIfNull(cacheOptions);

        var keyPrefix = ComposeKeyPrefix(cacheOptions.KeyPrefix, environmentId);

        var fusionCacheOptions = new FusionCacheOptions
        {
            CacheName = ComposeCacheName("Memory", keyPrefix),
            CacheKeyPrefix = keyPrefix,
            DistributedCacheKeyModifierMode = CacheKeyModifierMode.None,
            // Required for deterministic fail-safe source propagation in query builders.
            EnableSyncEventHandlersExecution = true,
            DefaultEntryOptions = EntryDefaults(cacheOptions, memoryOnly: true)
        };
        ConfigureTagEntries(fusionCacheOptions.TagsDefaultEntryOptions, memoryOnly: true);

        cacheOptions.ConfigureFusionCacheOptions?.Invoke(fusionCacheOptions);

        var fusion = new FusionCache(
            Options.Create(fusionCacheOptions),
            memoryCache,
            fusionCacheLogger);

        return new FusionCacheManager(
            fusion,
            CacheStorageMode.HydratedObject,
            cacheOptions.DefaultExpiration,
            fusionCacheOptions.CacheKeyPrefix ?? string.Empty,
            logger,
            WriteOptions(fusionCacheOptions, cacheOptions, memoryOnly: true));
    }

    /// <summary>
    /// Builds a manager over a distributed cache.
    /// </summary>
    /// <remarks>
    /// FusionCache always has a memory tier in front of the distributed one, and it is used either way -
    /// there is no distributed-only mode. Invalidation state is held per instance, so it reaches other
    /// nodes only over a backplane; without one, a second node keeps serving content this node has
    /// evicted until the entry expires. A backplane is therefore what makes multi-node invalidation work,
    /// and what keeps the memory tiers in step.
    /// </remarks>
    public static FusionCacheManager CreateHybrid(
        IDistributedCache distributedCache,
        DeliveryCacheOptions cacheOptions,
        JsonSerializerOptions? serializerOptions = null,
        ILogger? logger = null,
        IFusionCacheBackplane? backplane = null,
        string? environmentId = null,
        ILogger<FusionCache>? fusionCacheLogger = null)
    {
        ArgumentNullException.ThrowIfNull(distributedCache);
        ArgumentNullException.ThrowIfNull(cacheOptions);

        var keyPrefix = ComposeKeyPrefix(cacheOptions.KeyPrefix, environmentId);

        var fusionCacheOptions = new FusionCacheOptions
        {
            CacheName = ComposeCacheName("Hybrid", keyPrefix),
            // The version sits inside the prefix so that FusionCache's own keys carry it too.
            CacheKeyPrefix = keyPrefix + DistributedFormatVersion,
            // FusionCache would otherwise version the distributed keys itself; DistributedFormatVersion
            // covers its wire format as well, and keeps the keys readable.
            DistributedCacheKeyModifierMode = CacheKeyModifierMode.None,
            // Required for deterministic fail-safe source propagation in query builders.
            EnableSyncEventHandlersExecution = true,
            // A distributed cache that is down is worked around, not retried on every request: while the
            // breaker is open the memory tier and the origin carry the load, and FusionCache re-syncs the
            // distributed tier when it comes back.
            DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(2),
            DefaultEntryOptions = EntryDefaults(cacheOptions, memoryOnly: false)
        };
        ConfigureTagEntries(fusionCacheOptions.TagsDefaultEntryOptions, memoryOnly: false);

        cacheOptions.ConfigureFusionCacheOptions?.Invoke(fusionCacheOptions);

        var fusion = new FusionCache(
            Options.Create(fusionCacheOptions),
            memoryCache: null,
            fusionCacheLogger);

        // Falls back to the SDK's own serializer rather than plain defaults. What this tier stores is wire
        // types, and content type elements are polymorphic: without ContentElementConverter the L2 payload
        // is written by the declared type, silently dropping TaxonomyElement.TaxonomyGroup and
        // MultipleChoiceElement.Options, and every hit comes back as a base ContentElement.
        var serializer = new FusionCacheSystemTextJsonSerializer(
            serializerOptions ?? RefitSettingsProvider.CreateDefaultJsonSerializerOptions());
        fusion.SetupDistributedCache(distributedCache, serializer);

        if (backplane is not null)
        {
            fusion.SetupBackplane(backplane);
        }

        return new FusionCacheManager(
            fusion,
            CacheStorageMode.RawJson,
            cacheOptions.DefaultExpiration,
            fusionCacheOptions.CacheKeyPrefix ?? string.Empty,
            logger,
            WriteOptions(fusionCacheOptions, cacheOptions, memoryOnly: false));
    }

    /// <summary>
    /// The entry options the SDK starts from. They are what the consumer's
    /// <see cref="DeliveryCacheOptions.ConfigureFusionCacheOptions"/> callback sees as
    /// <see cref="FusionCacheOptions.DefaultEntryOptions"/>, and every write starts from whatever the
    /// callback leaves there.
    /// </summary>
    private static FusionCacheEntryOptions EntryDefaults(DeliveryCacheOptions cacheOptions, bool memoryOnly)
    {
        var options = new FusionCacheEntryOptions
        {
            Size = EntrySize,
            AllowBackgroundDistributedCacheOperations = false,
            AllowBackgroundBackplaneOperations = false
        };

        return Pin(options, cacheOptions, memoryOnly);
    }

    /// <summary>
    /// The options every write passes: the consumer's <see cref="FusionCacheOptions.DefaultEntryOptions"/>
    /// after the callback ran, with what the SDK decides re-applied on top. Anything else set there - a
    /// <see cref="FusionCacheEntryOptions.Size"/>, background backplane operations, a distributed-cache
    /// timeout - reaches the SDK's reads and writes.
    /// </summary>
    private static FusionCacheEntryOptions WriteOptions(FusionCacheOptions fusionCacheOptions, DeliveryCacheOptions cacheOptions, bool memoryOnly)
        => Pin(fusionCacheOptions.DefaultEntryOptions.Duplicate(), cacheOptions, memoryOnly);

    /// <summary>
    /// What the SDK decides regardless of the consumer's entry options: the tier skips in memory mode,
    /// which failures are thrown, and the timing and fail-safe policy that
    /// <see cref="DeliveryCacheOptions"/> owns.
    /// </summary>
    /// <remarks>
    /// A distributed-cache failure is worked around - the factory or the memory tier answers and
    /// FusionCache logs it - rather than thrown out of every cached query. A serialization failure is
    /// still thrown: that is a defect in the SDK's own payloads, and hiding it would hide the defect.
    /// </remarks>
    private static FusionCacheEntryOptions Pin(FusionCacheEntryOptions options, DeliveryCacheOptions cacheOptions, bool memoryOnly)
    {
        options.SkipDistributedCacheRead = memoryOnly;
        options.SkipDistributedCacheWrite = memoryOnly;
        options.ReThrowDistributedCacheExceptions = false;
        options.ReThrowSerializationExceptions = true;
        options.ReThrowBackplaneExceptions = false;

        options.Duration = cacheOptions.DefaultExpiration;
        options.IsFailSafeEnabled = cacheOptions.IsFailSafeEnabled;
        options.FailSafeMaxDuration = cacheOptions.FailSafeMaxDuration;
        options.FailSafeThrottleDuration = cacheOptions.FailSafeThrottleDuration;
        options.JitterMaxDuration = cacheOptions.JitterMaxDuration;

        if (cacheOptions.EagerRefreshThreshold > 0)
        {
            options.EagerRefreshThreshold = cacheOptions.EagerRefreshThreshold;
        }

        return options;
    }

    /// <summary>
    /// The options <c>RemoveByTag</c> and <c>Clear</c> run with, which is what the tag-expiration entries
    /// they write are stored with.
    /// </summary>
    /// <remarks>
    /// The <see cref="FusionCacheEntryOptions.Duration"/> is deliberately left at FusionCache's own
    /// default for tag data, ten days: it is how long an invalidation is remembered for an entry that
    /// has not been read since, so it has to outlive every entry it could apply to. Passing write options
    /// here instead would store the tag data for the entries' duration - with a bare
    /// <c>FusionCacheEntryOptions</c>, for thirty seconds - and a webhook's invalidation would be
    /// forgotten before a quiet entry was next read.
    /// </remarks>
    private static void ConfigureTagEntries(FusionCacheEntryOptions tagOptions, bool memoryOnly)
    {
        tagOptions.Size = EntrySize;
        tagOptions.IsFailSafeEnabled = false;
        tagOptions.SkipDistributedCacheRead = memoryOnly;
        tagOptions.SkipDistributedCacheWrite = memoryOnly;
        tagOptions.ReThrowDistributedCacheExceptions = false;
        tagOptions.ReThrowSerializationExceptions = false;
        tagOptions.ReThrowBackplaneExceptions = false;
        tagOptions.AllowBackgroundDistributedCacheOperations = false;
        tagOptions.AllowBackgroundBackplaneOperations = false;
    }

    public CacheStorageMode StorageMode => _storageMode;

    public async Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(factory);
        ThrowIfDisposed();

        // The only reliable way to tell whether the value we get back was produced by *this* call:
        // FusionCache runs the factory on a background thread for eager refresh while returning the
        // stale value immediately, so a flag set inside the factory says nothing about which call it
        // belongs to. The envelope instance does - it comes back only if this invocation produced it.
        CacheEnvelope<T>? producedHere = null;
        var observation = new StaleObservation(cacheKey, _keyPrefix + cacheKey);
        CurrentCall.Value = observation;

        try
        {
            var envelope = await _cache.GetOrSetAsync<CacheEnvelope<T>>(
                cacheKey,
                async (ctx, ct) =>
                {
                    var factoryResult = await factory(ct).ConfigureAwait(false);

                    if (factoryResult is null)
                    {
                        // The origin has no value for this key. Fail-safe is for an origin that cannot be
                        // reached, which arrives as a thrown exception; an answer must not be papered over
                        // with a stale copy, so this call opts out of it and the copy is removed below.
                        ctx.Options.IsFailSafeEnabled = false;
                        throw new CacheFactoryFailedException();
                    }

                    // Dependency keys serve two purposes:
                    // 1. FusionCache tags — used for cache invalidation.
                    // 2. Stored in CacheEnvelope alongside the value — surfaced to consumers via CacheResult<T>.
                    var deps = factoryResult.Dependencies
                        .Where(d => !string.IsNullOrWhiteSpace(d))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    ctx.Tags = deps;
                    ctx.Options.Duration = expiration ?? _defaultExpiration;
                    return producedHere = new CacheEnvelope<T>(factoryResult.Value, deps);
                },
                _baseWriteOptions,
                token: cancellationToken).ConfigureAwait(false);

            if (envelope is null)
                return null;

            var fromFactory = ReferenceEquals(envelope, producedHere);

            return new CacheResult<T>(envelope.Value, envelope.DependencyKeys)
            {
                FromFactory = fromFactory,
                IsStale = !fromFactory && observation.Stale,
            };
        }
        catch (CacheFactoryFailedException)
        {
            // Opting out of fail-safe keeps the stale copy in the store, where the next call with
            // fail-safe on would find it - so it goes explicitly.
            await _cache.RemoveAsync(cacheKey, _baseWriteOptions, cancellationToken).ConfigureAwait(false);
            return null;
        }
        finally
        {
            CurrentCall.Value = null;
        }
    }

    /// <summary>
    /// Sentinel thrown inside the FusionCache factory when the upstream factory returns <c>null</c>, so
    /// FusionCache abandons the call without storing anything. It never leaves
    /// <see cref="GetOrSetAsync{T}"/> - it is caught immediately after the <c>GetOrSetAsync</c> call.
    /// </summary>
#pragma warning disable S3871 // Intentionally private sentinel — never leaves this class
    private sealed class CacheFactoryFailedException : Exception;
#pragma warning restore S3871

    /// <summary>
    /// What the event handlers record for the call in flight: whether FusionCache served it a stale
    /// value. The key is compared in both the form the SDK passed and the form FusionCache stores.
    /// </summary>
    private sealed class StaleObservation(string key, string prefixedKey)
    {
        public bool Stale { get; private set; }

        public void Observe(string eventKey)
        {
            if (eventKey == key || eventKey == prefixedKey)
            {
                Stale = true;
            }
        }
    }

    public async Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (dependencyKeys is null || dependencyKeys.Length == 0)
        {
            return true;
        }

        // Tags are compared ordinally and the SDK composes them lower-case; a key that arrives in another
        // casing - copied from a webhook payload, say - would otherwise silently match nothing.
        var validKeys = dependencyKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .ToArray();

        if (_logger is not null && validKeys.Length > 0)
            LoggerMessages.CacheInvalidateStarting(_logger, validKeys.Length);

        try
        {
            // No options: FusionCache then uses TagsDefaultEntryOptions, which ConfigureTagEntries set up.
            await _cache.RemoveByTagAsync(
                    validKeys,
                    options: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (_logger is not null)
            {
                foreach (var dependencyKey in validKeys)
                {
                    LoggerMessages.CacheInvalidateCompleted(_logger, dependencyKey);
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LoggerMessages.CacheInvalidationFailed(_logger, ex);

            return false;
        }
    }

    public async Task PurgeAsync(bool allowFailSafe = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _cache.ClearAsync(
                allowFailSafe,
                options: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _cache.Events.FailSafeActivate -= _failSafeActivateHandler;
        _cache.Events.Hit -= _hitHandler;
        _cache.Dispose();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, nameof(FusionCacheManager));

    private static void HandleFailSafeActivate(object? sender, FusionCacheEntryEventArgs eventArgs)
        => CurrentCall.Value?.Observe(eventArgs.Key);

    private static void HandleHit(object? sender, FusionCacheEntryHitEventArgs eventArgs)
    {
        if (eventArgs.IsStale)
        {
            CurrentCall.Value?.Observe(eventArgs.Key);
        }
    }
}
