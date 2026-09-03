using System.Collections.Concurrent;
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
/// Shared FusionCache-backed implementation of SDK cache manager behavior.
/// </summary>
internal sealed class FusionCacheManager : IDeliveryCacheManager, IDeliveryCachePurger, IFailSafeStateProvider, IDisposable
{
    private readonly IFusionCache _cache;
    private readonly CacheStorageMode _storageMode;
    private readonly TimeSpan _defaultExpiration;
    private readonly Func<string, string> _cacheKeyFormatter;
    private readonly Func<string, string> _dependencyTagFormatter;
    private readonly ILogger? _logger;
    private readonly FusionCacheEntryOptions _baseWriteOptions;
    private readonly FusionCacheEntryOptions _baseInvalidateOptions;
    private readonly ConcurrentDictionary<string, byte> _failSafeActiveKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Hard cap for <see cref="_failSafeActiveKeys"/>. In hybrid mode (L2-only, no L1 memory cache),
    /// memory eviction events never fire, so entries can accumulate if they enter fail-safe but are
    /// never re-requested or invalidated. Clearing at this threshold is safe because
    /// <see cref="IFailSafeStateProvider.IsFailSafeActive"/> is metadata-only (affects ResponseSource,
    /// not correctness) and stale entries will be re-tracked on the next stale hit.
    /// </summary>
    private const int FailSafeTrackingCapacity = 10_000;
    private readonly EventHandler<FusionCacheEntryEventArgs> _failSafeActivateHandler;
    private readonly EventHandler<FusionCacheEntryEventArgs> _factorySuccessHandler;
    private readonly EventHandler<FusionCacheEntryHitEventArgs> _hitHandler;
    private readonly EventHandler<FusionCacheEntryEventArgs> _removeHandler;
    private readonly EventHandler<FusionCacheEntryEvictionEventArgs> _evictionHandler;
    private int _disposeState;

    private FusionCacheManager(
        IFusionCache cache,
        CacheStorageMode storageMode,
        TimeSpan defaultExpiration,
        Func<string, string> cacheKeyFormatter,
        Func<string, string> dependencyTagFormatter,
        ILogger? logger,
        FusionCacheEntryOptions baseWriteOptions,
        FusionCacheEntryOptions baseInvalidateOptions)
    {
        _cache = cache;
        _storageMode = storageMode;
        _defaultExpiration = defaultExpiration;
        _cacheKeyFormatter = cacheKeyFormatter;
        _dependencyTagFormatter = dependencyTagFormatter;
        _logger = logger;
        _baseWriteOptions = baseWriteOptions;
        _baseInvalidateOptions = baseInvalidateOptions;

        _failSafeActivateHandler = HandleFailSafeActivate;
        _factorySuccessHandler = HandleFactorySuccess;
        _hitHandler = HandleHit;
        _removeHandler = HandleRemove;
        _evictionHandler = HandleEviction;
        SubscribeFailSafeStateEvents();
    }

    /// <summary>
    /// Builds the segment every cache key and dependency tag is prefixed with.
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

    public static FusionCacheManager CreateMemory(
        IMemoryCache memoryCache,
        DeliveryCacheOptions cacheOptions,
        ILogger? logger = null,
        string? environmentId = null)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        ArgumentNullException.ThrowIfNull(cacheOptions);

        var effectiveExpiration = cacheOptions.DefaultExpiration;
        var keyPrefix = cacheOptions.KeyPrefix;
        var prefixSegment = ComposeKeyPrefix(keyPrefix, environmentId);

        var defaultEntryOptions = new FusionCacheEntryOptions
        {
            AllowBackgroundDistributedCacheOperations = false,
            AllowBackgroundBackplaneOperations = false,
            ReThrowDistributedCacheExceptions = false,
            ReThrowSerializationExceptions = true,
            ReThrowBackplaneExceptions = false
        };
        ApplyCachePolicy(defaultEntryOptions, cacheOptions, effectiveExpiration);

        var fusionCacheOptions = new FusionCacheOptions
        {
            CacheName = $"KontentDelivery.Memory.{(string.IsNullOrWhiteSpace(keyPrefix) ? "Default" : keyPrefix)}",
            DistributedCacheKeyModifierMode = CacheKeyModifierMode.None,
            // Required for deterministic fail-safe source propagation in query builders.
            EnableSyncEventHandlersExecution = true,
            DefaultEntryOptions = defaultEntryOptions
        };

        cacheOptions.ConfigureFusionCacheOptions?.Invoke(fusionCacheOptions);

        var fusion = new FusionCache(
            Options.Create(fusionCacheOptions),
            memoryCache,
            logger: null);

        var baseWriteOptions = new FusionCacheEntryOptions
        {
            SkipDistributedCacheRead = true,
            SkipDistributedCacheWrite = true,
            ReThrowDistributedCacheExceptions = false,
            ReThrowSerializationExceptions = true,
            ReThrowBackplaneExceptions = false,
            AllowBackgroundBackplaneOperations = false,
            AllowBackgroundDistributedCacheOperations = false
        };
        ApplyCachePolicy(baseWriteOptions, cacheOptions, effectiveExpiration);

        return new FusionCacheManager(
            fusion,
            CacheStorageMode.HydratedObject,
            effectiveExpiration,
            cacheKey => $"{prefixSegment}{cacheKey}",
            dependency => $"{prefixSegment}{dependency}",
            logger,
            baseWriteOptions: baseWriteOptions,
            baseInvalidateOptions: new FusionCacheEntryOptions
            {
                IsFailSafeEnabled = false,
                SkipDistributedCacheRead = true,
                SkipDistributedCacheWrite = true,
                ReThrowDistributedCacheExceptions = false,
                ReThrowSerializationExceptions = false,
                ReThrowBackplaneExceptions = false,
                AllowBackgroundBackplaneOperations = false,
                AllowBackgroundDistributedCacheOperations = false
            });
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
        string? environmentId = null)
    {
        ArgumentNullException.ThrowIfNull(distributedCache);
        ArgumentNullException.ThrowIfNull(cacheOptions);

        var effectiveExpiration = cacheOptions.DefaultExpiration;
        var keyPrefix = cacheOptions.KeyPrefix;
        var prefixSegment = ComposeKeyPrefix(keyPrefix, environmentId);

        var defaultEntryOptions = new FusionCacheEntryOptions
        {
            AllowBackgroundDistributedCacheOperations = false,
            AllowBackgroundBackplaneOperations = false,
            ReThrowDistributedCacheExceptions = false,
            ReThrowSerializationExceptions = true,
            ReThrowBackplaneExceptions = false
        };
        ApplyCachePolicy(defaultEntryOptions, cacheOptions, effectiveExpiration);

        var fusionCacheOptions = new FusionCacheOptions
        {
            CacheName = $"KontentDelivery.Hybrid.{(string.IsNullOrWhiteSpace(keyPrefix) ? "Default" : keyPrefix)}",
            DistributedCacheKeyModifierMode = CacheKeyModifierMode.None,
            // Required for deterministic fail-safe source propagation in query builders.
            EnableSyncEventHandlersExecution = true,
            DefaultEntryOptions = defaultEntryOptions
        };

        cacheOptions.ConfigureFusionCacheOptions?.Invoke(fusionCacheOptions);

        var fusion = new FusionCache(
            Options.Create(fusionCacheOptions),
            memoryCache: null,
            logger: null);

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

        var baseWriteOptions = new FusionCacheEntryOptions
        {
            ReThrowDistributedCacheExceptions = true,
            ReThrowSerializationExceptions = true,
            ReThrowBackplaneExceptions = false,
            AllowBackgroundBackplaneOperations = false,
            AllowBackgroundDistributedCacheOperations = false
        };
        ApplyCachePolicy(baseWriteOptions, cacheOptions, effectiveExpiration);

        return new FusionCacheManager(
            fusion,
            CacheStorageMode.RawJson,
            effectiveExpiration,
            cacheKey => $"{prefixSegment}cache:{cacheKey}",
            dependency => $"{prefixSegment}dep:{dependency}",
            logger,
            baseWriteOptions: baseWriteOptions,
            baseInvalidateOptions: new FusionCacheEntryOptions
            {
                IsFailSafeEnabled = false,
                ReThrowDistributedCacheExceptions = false,
                ReThrowSerializationExceptions = false,
                ReThrowBackplaneExceptions = false,
                AllowBackgroundBackplaneOperations = false,
                AllowBackgroundDistributedCacheOperations = false
            });
    }

    public CacheStorageMode StorageMode => _storageMode;

    public async Task<CacheResult<T>?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<CacheEntry<T>?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            var entry = await factory(cancellationToken).ConfigureAwait(false);
            if (entry is null)
                return null;

            var deps = entry.Dependencies
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new CacheResult<T>(entry.Value, deps) { FromFactory = true };
        }

        var formattedKey = _cacheKeyFormatter(cacheKey);

        // The only reliable way to tell whether the value we get back was produced by *this* call:
        // FusionCache runs the factory on a background thread for eager refresh while returning the
        // stale value immediately, so a flag set inside the factory says nothing about which call it
        // belongs to. The envelope instance does - it comes back only if this invocation produced it.
        CacheEnvelope<T>? producedHere = null;

        try
        {
            var envelope = await _cache.GetOrSetAsync<CacheEnvelope<T>>(
                formattedKey,
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
                    // 1. FusionCache tags (formatted via _dependencyTagFormatter) — used for cache invalidation.
                    // 2. Stored in CacheEnvelope alongside the value — surfaced to consumers via CacheResult<T>.
                    var deps = factoryResult.Dependencies
                        .Where(d => !string.IsNullOrWhiteSpace(d))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    ctx.Tags = Array.ConvertAll(deps, _dependencyTagFormatter.Invoke);
                    ctx.Options.Duration = expiration ?? _defaultExpiration;
                    _failSafeActiveKeys.TryRemove(formattedKey, out var _);
                    _failSafeActiveKeys.TryRemove(cacheKey, out var _);
                    return producedHere = new CacheEnvelope<T>(factoryResult.Value, deps);
                },
                _baseWriteOptions,
                token: cancellationToken).ConfigureAwait(false);

            if (envelope is null)
                return null;

            return new CacheResult<T>(envelope.Value, envelope.DependencyKeys)
            {
                FromFactory = ReferenceEquals(envelope, producedHere),
            };
        }
        catch (CacheFactoryFailedException)
        {
            // Opting out of fail-safe keeps the stale copy in the store, where the next call with
            // fail-safe on would find it - so it goes explicitly.
            _failSafeActiveKeys.TryRemove(formattedKey, out var _);
            await _cache.RemoveAsync(formattedKey, _baseInvalidateOptions, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch
        {
            // Factory threw and no stale entry was available for fail-safe.
            _failSafeActiveKeys.TryRemove(formattedKey, out var _);
            throw;
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

    public async Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (dependencyKeys is null || dependencyKeys.Length == 0)
        {
            return true;
        }

        var validKeys = Array.TrueForAll(dependencyKeys, k => !string.IsNullOrWhiteSpace(k))
            ? dependencyKeys
            : dependencyKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();

        if (_logger is not null && validKeys.Length > 0)
            LoggerMessages.CacheInvalidateStarting(_logger, validKeys.Length);

        try
        {
            await _cache.RemoveByTagAsync(
                    validKeys.Select(_dependencyTagFormatter),
                    _baseInvalidateOptions,
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
                _baseInvalidateOptions,
                cancellationToken)
            .ConfigureAwait(false);

        // Only clear fail-safe tracking when entries are permanently removed.
        // When allowFailSafe is true, entries remain for fail-safe and should
        // continue to be reported as ResponseSource.FailSafe.
        if (!allowFailSafe)
        {
            _failSafeActiveKeys.Clear();
        }
    }

    bool IFailSafeStateProvider.IsFailSafeActive(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            return false;
        }

        return _failSafeActiveKeys.ContainsKey(cacheKey)
            || _failSafeActiveKeys.ContainsKey(_cacheKeyFormatter(cacheKey));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        UnsubscribeFailSafeStateEvents();
        _cache.Dispose();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, nameof(FusionCacheManager));

    /// <summary>
    /// Applies fail-safe, jitter, and eager-refresh policy from <see cref="DeliveryCacheOptions"/>
    /// to a <see cref="FusionCacheEntryOptions"/> instance.
    /// </summary>
    private static void ApplyCachePolicy(
        FusionCacheEntryOptions options,
        DeliveryCacheOptions cacheOptions,
        TimeSpan duration)
    {
        options.Duration = duration;
        options.IsFailSafeEnabled = cacheOptions.IsFailSafeEnabled;
        options.FailSafeMaxDuration = cacheOptions.FailSafeMaxDuration;
        options.FailSafeThrottleDuration = cacheOptions.FailSafeThrottleDuration;
        options.JitterMaxDuration = cacheOptions.JitterMaxDuration;

        if (cacheOptions.EagerRefreshThreshold > 0)
        {
            options.EagerRefreshThreshold = cacheOptions.EagerRefreshThreshold;
        }
    }

    private void SubscribeFailSafeStateEvents()
    {
        _cache.Events.FailSafeActivate += _failSafeActivateHandler;
        _cache.Events.FactorySuccess += _factorySuccessHandler;
        _cache.Events.Hit += _hitHandler;
        _cache.Events.Remove += _removeHandler;
        _cache.Events.Memory.Eviction += _evictionHandler;
    }

    private void UnsubscribeFailSafeStateEvents()
    {
        _cache.Events.FailSafeActivate -= _failSafeActivateHandler;
        _cache.Events.FactorySuccess -= _factorySuccessHandler;
        _cache.Events.Hit -= _hitHandler;
        _cache.Events.Remove -= _removeHandler;
        _cache.Events.Memory.Eviction -= _evictionHandler;
    }

    private void HandleFailSafeActivate(object? sender, FusionCacheEntryEventArgs eventArgs)
    {
        if (_failSafeActiveKeys.Count >= FailSafeTrackingCapacity)
            _failSafeActiveKeys.Clear();

        _failSafeActiveKeys[eventArgs.Key] = 1;
    }

    private void HandleFactorySuccess(object? sender, FusionCacheEntryEventArgs eventArgs)
        => _failSafeActiveKeys.TryRemove(eventArgs.Key, out var _);

    private void HandleHit(object? sender, FusionCacheEntryHitEventArgs eventArgs)
    {
        if (eventArgs.IsStale)
        {
            _failSafeActiveKeys[eventArgs.Key] = 1;
            return;
        }

        _failSafeActiveKeys.TryRemove(eventArgs.Key, out var _);
    }

    private void HandleRemove(object? sender, FusionCacheEntryEventArgs eventArgs)
        => _failSafeActiveKeys.TryRemove(eventArgs.Key, out var _);

    private void HandleEviction(object? sender, FusionCacheEntryEvictionEventArgs eventArgs)
        => _failSafeActiveKeys.TryRemove(eventArgs.Key, out var _);
}
