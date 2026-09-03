# Delivery's caching layer

Analysis of `Kontent.Ai.Delivery.Caching` (`FusionCacheManager`, the two managers over it, the
builder registrations) and the core's cache path (`CacheKeyBuilder`, `CachePayloadHelper`,
`CachedQueryExecutor`, `DependencyTrackingContext`, the query builders' cache branches). First
written 2026-09-03 against `client-builders` at `dddd4f8fe`; revised the same day against
`18a283435` after a second, independent pass. This is the area `docs/delivery-rich-text-analysis.md`
§8 recorded as never traced.

> [!NOTE]
> Every behavioural claim below was checked against the code, and the ones that could not be settled
> by reading were verified by running throwaway probes against the built assemblies. The first pass
> drove the internal managers directly; the second drove the public surface the way a consumer would -
> `AddDeliveryClient` with `UseMemoryCache` / `UseHybridCache`, `IDeliveryClient`, the keyed
> `IDeliveryCacheManager`, `IDeliveryCachePurger` - over a shared `MemoryCache`, a shared
> `MemoryDistributedCache` standing in for Redis, and stub distributed caches that throw or record keys.
> The probes are listed in §6 so each finding can be re-run or turned into a test. Nothing was changed.

> [!IMPORTANT]
> **Revision, 2026-09-03.** The second pass kept the first one's structure and most of its findings,
> and changed the following. Each change says why.
>
> - **Added 2.1, fail-safe serving content the origin says is gone.** The first pass recorded that an
>   invalidated entry stays eligible for fail-safe (now 2.11) and called it arguably right. Probed
>   end to end it is not: with fail-safe on, an item that a webhook evicted and the API then reports
>   as `404` is served as a success for up to `FailSafeMaxDuration`. Unpublishing does not take
>   effect. That is a correctness defect, so it moved to the top.
> - **Corrected the purge finding (2.4) and the "what is right" bullet it contradicted.** The first
>   pass reported purge as isolated over a shared memory cache and leaking only to a fresh node over a
>   shared distributed cache. The second pass found it leaking over a shared `IMemoryCache` as well,
>   and which of the other client's nodes loses its entries depends on which reads first, not on
>   warm versus fresh. The memory setup is the one the caching guide recommends for multi-tenant
>   applications, so the severity went up.
> - **Added 2.5 and 2.6**, both found by reading the consumer-facing path rather than the manager:
>   the default client's cache manager is reachable only through an internal string and a standalone
>   client's not at all, and FusionCache runs with its logger disabled so the outages 2.2 asks it to
>   swallow would be swallowed silently.
> - **Added §3 (multi-client isolation, stated once) and §4 (what to simplify).** The question this
>   document was asked to answer is whether multi-client setups collide and whether the design carries
>   more than it needs; the first pass answered the first in passing and the second not at all.
> - **Extended 2.8** with the wire-format prefix FusionCache would have added and the SDK switched
>   off, since it is the same hazard from the other side.
> - Renumbered; the §6 probe table now carries both passes, with the two rows the second pass
>   overturned marked.
> - **Added 2.13 during implementation**: an invalidation was forgotten after thirty seconds for any
>   entry not read in that window, because the tag data was stored with the write options' default
>   duration. Neither pass caught it - every probe read the entry straight after invalidating it. It
>   outranks everything but 2.1 and was fixed in the step that restructures the entry options.

**Verdict: the design is sound where it meets the API - tag invalidation is complete for typed
models and isolated per client and per environment, the backplane is wired the way FusionCache
intends, keys are deterministic and environment-scoped, and stampedes are coalesced. It is unsound
in two places where it meets things it does not own. Fail-safe cannot tell an outage from a
definitive answer, so with it on, unpublished content keeps being served. And FusionCache's own
bookkeeping keys are not namespaced, so a purge on one client empties every client sharing the
store, memory or Redis. Around those, a distributed-cache outage or a size-limited memory cache
throws out of every cached query instead of degrading, the documented FusionCache escape hatch does
not reach the SDK's writes, and a payload-shape change between releases turns every stale Redis hit
into an exception. Each has a small fix, and none moves the public surface except where a fix is
an addition.**

---

## 1. What is right

Recorded first so the findings read against the whole.

- **Tag invalidation is isolated per client and per environment.** Dependency tags carry the same
  `{KeyPrefix}:{EnvironmentId}:` segment as the keys, so `InvalidateAsync(["item_x"])` on one
  client never evicts another's entries - probed over a shared `IMemoryCache`, over a shared
  distributed cache, and over a shared backplane channel; in every case the other client kept
  serving its own value (probes 1b, 2, 12).
- **The backplane is registered and consumed correctly.** Both FusionCache backplane packages
  register `IFusionCacheBackplane` as *transient* (probes 4, 5, 20), so each hybrid manager gets an
  instance of its own; `SetupBackplane` is called once per FusionCache; the SDK never subscribes two
  caches to one backplane object. Cross-node invalidation with a backplane is pinned by
  `HybridCacheCoherenceTests` and the Redis integration test.
- **Disposing a manager does not dispose the application's `IMemoryCache`** (probe 10). FusionCache
  disposes only what it created.
- **The preview path never touches the cache.** `DeliveryClient.GetEffectiveCacheManager` returns
  null whenever `UsePreviewApi` is on, read per request, so preview and production content cannot
  cross in a shared store even under one environment id.
- **Dependency tracking is complete for typed models.** Items, their types, modular-content items
  (components filtered by the `workflow`/`workflow_step` signal, their *types* still tracked), linked
  items elements, asset elements (id parsed from the URL), rich-text inline images, content links and
  inline items, and taxonomy groups - plus the list scope on every listing. The fixture item
  `coffee_beverages_explained` yields eight keys: its own item and type, three linked items, two
  linked types, one asset, one taxonomy group (probe 15). Type tags on item caches are what makes a
  content-type webhook evict item responses, as the interface doc promises.
- **Concurrent misses are coalesced.** Three simultaneous first reads of one key produced one origin
  call; the two waiters were reported as `ResponseSource.Cache` (probe 16), which is the right
  answer for callers that did not fetch.
- **The `FromFactory` envelope trick is the right answer to eager refresh**, and
  `CachedQueryExecutor` reads staleness from the manager rather than from factory locals.
  `EnableSyncEventHandlersExecution` is what makes that deterministic. Per-query expiration is set
  on the factory context's duplicated options and does not leak into the manager's shared write
  options (probe 14).
- **The hybrid tier serializes with the SDK's own options**, so polymorphic content-type elements
  survive Redis (`18b964bd0`), and `DistributedCacheKeyModifierMode.None` keeps Redis keys readable
  - see 2.8 for what that trades away.
- **Keys are deterministic and order-independent**, filters hashed with 72 bits, element
  projections sorted, the model type discriminating hydrated entries and *not* raw-JSON ones so a
  distributed cache is shared across model types. The environment id is in the prefix (`9558d0124`).

## 2. Findings

Ordered by consequence. Severity is what a consumer experiences, not code size.

### 2.1 Fail-safe serves content the origin says is gone - **high**

Every cached query's factory returns `null` from `CacheEntry<T>?` when the API call fails, and
`FusionCacheManager.GetOrSetAsync` turns that null into a thrown sentinel so FusionCache's fail-safe
can serve a stale copy (`FusionCacheManager.cs:293-296`). The factory does not say *why* the call
failed, and the manager cannot ask: a `503` and a `404` arrive as the same null.

Probed end to end through the public surface (probe 13): cache an item with fail-safe on, invalidate
it as a webhook would, have the origin answer `404` from then on. Every subsequent read returns
`IsSuccess = true` with `ResponseSource.FailSafe`, and the origin is asked on every one of them
(throttle set to zero for the probe) and says `404` every time. The same happens after natural
expiry with no invalidation at all. With fail-safe *off* the same sequence returns the `404` as a
failed result, as it should. So for a consumer who turned fail-safe on, unpublishing an item, deleting
a taxonomy group or removing a type stops taking effect for `FailSafeMaxDuration` - a day by default
- and the webhook that was supposed to make it take effect is what puts the entry into the state
where fail-safe applies (2.11).

FusionCache's `RemoveByTag` under fail-safe is an *expire*, not a *remove* (its own doc says so), so
this is not a FusionCache bug: fail-safe is doing what it is for. The SDK is feeding it definitive
answers as if they were outages.

**Fix.** Two parts, both small. First, the factory has to distinguish. `HttpRetryPredicates.IsRetryableStatusCode`
and `IsTransientException` are already compiled into the assembly and are the SDK's own definition of
transient: a transport exception, `408`, `429` or `5xx` is an outage; any other failure is the
origin's answer. Second, the manager has to act on it. Probed against FusionCache directly (probe
21): a factory that sets `ctx.Options.IsFailSafeEnabled = false` before throwing has its exception
propagated rather than a stale value served, but the stale copy stays in the store and the next
call with fail-safe on serves it, so the manager must also `RemoveAsync(key)` after such a failure.

How the factory tells the manager is a contract choice. The minimal one keeps `IDeliveryCacheManager`
as it is and documents the meaning it already half has: a factory that returns `null` means *the
origin has no value* - do not cache, and drop any stale copy - and a factory that *throws* means it
could not reach the origin, so serve stale if there is one. The query builders then throw an internal
exception for transient failures, carrying the failed result, and catch it after `GetOrSetAsync`
returns; `EnsureApiResult` already covers the coalesced waiters that have no captured result. Custom
managers that just await the factory are unaffected: exceptions propagate through them as they do
today. The more explicit alternative is a three-way outcome type in place of `CacheEntry<T>?`
(`Entry`, `Unavailable`, `Gone`), which is honest about the three cases but changes the interface a
second time in one release. Either way, add the probe as a test in both hosting modes and for both
`RemoveByTag` and natural expiry, and say in `DeliveryCacheOptions.IsFailSafeEnabled` that fail-safe
covers outages, not absence.

### 2.2 A distributed-cache outage throws out of every cached query - **high**

`FusionCacheManager.CreateHybrid` sets `ReThrowDistributedCacheExceptions = true` on the write
options every `GetOrSetAsync` passes (`FusionCacheManager.cs:226`). FusionCache's own default is
`false`: on a failed L2 read it logs, falls through to the factory (or the memory tier), and lets
auto-recovery re-sync the distributed tier later. With `true`, the L2 failure is thrown.

Probed with an `IDistributedCache` that throws (probes 6 and 18): every `GetTypes().ExecuteAsync()`
threw `FusionCacheDistributedCacheException` out of the query, with fail-safe off **and** on -
fail-safe does not help, because the exception is raised before the factory runs. Nothing above the
manager catches it: `CachedQueryExecutor` awaits the fetch unguarded and the query builders return it
to the caller as an exception, not as a failed result - the one place in the SDK where a transport
problem is not a result. So a Redis outage takes down every cached query of every hybrid client,
which is the opposite of what a cache is for, and the guide's "Redis Connection Failures"
troubleshooting entry (`caching-guide.md:1369`) suggests a consumer-side `try/catch` around
`_cache.GetAsync` that does not exist in this SDK, catching a `RedisConnectionException` that
FusionCache has already wrapped.

The setting dates from `cf142a851` (adopt FusionCache, 2026-02-17) with no recorded reason; the
neighbouring `ReThrowSerializationExceptions = true` has one (a serializer bug should surface) and
this one was most likely set alongside it. `InvalidateAsync` is already the other way round: it
swallows and returns `false`.

**Fix.** `ReThrowDistributedCacheExceptions = false` on the hybrid write options; keep it `false` on
the invalidate options as it is. Set `DistributedCacheCircuitBreakerDuration` (a few seconds) so a
dead Redis is not retried on every request, and pass FusionCache a logger (2.6) so the degradation
is visible. Add a test with a throwing `IDistributedCache` asserting the factory value is served,
with and without fail-safe. Rewrite the troubleshooting entry to say what actually happens.

### 2.3 A memory cache with a size limit throws on every write - **high**

`UseMemoryCache` takes the application's `IMemoryCache` (`AddMemoryCache()` is a `TryAdd`). If the
application configured `MemoryCacheOptions.SizeLimit`, `MemoryCache` requires every entry to declare a
`Size`, and FusionCache sets none unless `FusionCacheEntryOptions.Size` is set. Probed (probes 8 and
19): every `GetOrSetAsync` threw `InvalidOperationException: Cache entry must specify a value for
Size when SizeLimit is set`, on first write and on every retry. The exception propagates to the
caller like 2.2's.

The caching guide recommends exactly this configuration twice - "Advanced Memory Cache
Configuration" (`caching-guide.md:166`) and the memory-pressure troubleshooting entry (`:1352`) both
show `services.AddMemoryCache(options => options.SizeLimit = 1024)`. A consumer who follows the
guide gets a cache that throws.

**Fix.** Set `Size = 1` on every entry-options object the manager builds (write, default, invalidate,
and `TagsDefaultEntryOptions`, since tag-expiration entries are memory entries too), and say in
`DeliveryCacheOptions` docs that entries count as one unit each under a size limit. Alternatively let
FusionCache own a private memory tier (`memoryCache: null`) - but the shared `IMemoryCache` is the
documented behaviour and worth keeping. Add a test over a size-limited `MemoryCache`.

### 2.4 Purge empties every client that shares the store - **high**

`PurgeAsync` is FusionCache's `Clear`, implemented as a clear timestamp stored under an internal key.
The SDK prefixes its own keys and tags, but FusionCache's internal keys are not prefixed: the probes
recorded the Redis keys a two-client setup produces (probe 2), and next to the namespaced
`a:{env}:cache:types` and `__fc:t:b:{env}:dep:type_article` sits a bare `__fc:t:!` - the clear
marker, shared by every FusionCache instance that shares the store.

Corrected from the first pass, which reported the leak as distributed-only and as reaching only a
fresh node. Probed through the public surface over one `MemoryDistributedCache` (probes 2, 2b) and
over one `IMemoryCache` (probe 1): after client `a` purges, client `b` goes to the origin too.
Over the distributed cache, which of `b`'s nodes loses its entries depends on which reads first after
the purge - in one ordering the warm node, in the other the fresh one - because the clear marker is
read through the shared tier and the first reader then repopulates it. Over a shared memory cache
both clients lose everything, since the marker is one entry in one `MemoryCache`. Tag invalidation
was isolated in every ordering (probe 1b).

Why the severity went up: the shared-`IMemoryCache` setup with a prefix per client is what the guide's
"Cache Key Prefixing", "Multi-Tenant Caching" and "Per-Client Caching" sections recommend, and an
operator who purges one tenant after a content-model change empties the others. The effect is extra
origin calls rather than stale content, but it is the kind of cross-tenant coupling the prefixes
exist to rule out.

**Fix.** Set `FusionCacheOptions.CacheKeyPrefix` to the SDK's prefix segment
(`{KeyPrefix}:{EnvironmentId}:`) and stop prefixing keys and tags by hand - FusionCache then
prefixes its internal keys with it too, and its `Clear` doc says outright that it is designed for
shared caches with a key prefix. Probed (probes 3, 2b, 1): with the prefix set per client, the other
client kept every entry after a purge in both stores, while the purging client's own fresh node
correctly saw the purge and a later tag invalidation. Also put the environment id into `CacheName`,
which names the backplane channel: today two default clients on different environments share
`KontentDelivery.Hybrid.Default` and see each other's notifications (harmless once keys are
namespaced, but noise). Add a two-client purge test over a shared `MemoryCache` and over a shared
`MemoryDistributedCache` with a fresh node.

### 2.5 The cache manager has no public name for the default client, and none for a standalone one - **medium**

`RegisterCacheManager` registers the manager as a keyed singleton under the client name and nothing
else (`DeliveryClientBuilderCachingExtensions.cs:110-115`). For a named client that is the documented
route: `GetRequiredKeyedService<IDeliveryCacheManager>("production")`, which every invalidation
example in the guide and README uses. For the *default* client - `AddDeliveryClient(delivery =>
delivery.UseMemoryCache())`, the most common registration - the key is `NamedClients.Default`, an
internal constant with the value `"Default"`, and the unkeyed `IDeliveryCacheManager` resolves to
nothing (probe 15). Neither the guide nor the README shows the default-client case; a consumer who
follows them with a default client has no way to write the webhook handler other than guessing the
string.

A client built with `DeliveryClient.Create(delivery => delivery.UseMemoryCache())` is worse off: the
manager lives in the private container and `DeliveryClient` exposes nothing that reaches it, so the
cache can be filled but never invalidated or purged. The only standalone option is `UseCacheManager`
with an instance the caller keeps hold of.

**Fix.** Register an unkeyed alias for the default client the way `AddClientServices` does for the
client itself - `TryAddSingleton(sp => sp.GetRequiredKeyedService<IDeliveryCacheManager>(Default))`
- so `GetRequiredService<IDeliveryCacheManager>()` is the default-client route and the string never
appears. Expose the manager on the standalone client, `IDeliveryCacheManager? DeliveryClient.Cache`
or similar, so `Create` users can invalidate. Show both in the guide's invalidation sections next to
the named form.

### 2.6 FusionCache runs with its logging switched off - **medium**

Both factories construct FusionCache with `logger: null` (`FusionCacheManager.cs:125` and `:209`).
FusionCache's constructor doc: "if null, logging will be completely disabled". Everything it would
otherwise report - a distributed-cache read that failed and was worked around, a backplane publish
that failed, a background eager-refresh factory that threw, auto-recovery kicking in - is invisible.
The manager's own `ILogger<MemoryCacheManager>` is used only for the SDK's invalidation messages.

This matters most once 2.2 is fixed: swallowing the L2 exception is right, swallowing it silently is
not. It also explains why the guide's "Logging" section offers a hand-written wrapper rather than
pointing at FusionCache's own categories.

**Fix.** Resolve `ILoggerFactory` in the `Use…` extensions and pass `CreateLogger<FusionCache>()` to
both factories (`ILogger<FusionCache>` is what the constructor takes). Say in the guide which
categories to enable: `ZiggyCreatures.Caching.Fusion.FusionCache` for FusionCache, the manager's for
the SDK.

### 2.7 `ConfigureFusionCache` does not reach the SDK's writes - **medium**

Every `GetOrSetAsync` passes `_baseWriteOptions`, an options object the manager builds itself;
`RemoveByTagAsync` and `ClearAsync` pass `_baseInvalidateOptions`. FusionCache uses the options it
is handed and consults `DefaultEntryOptions` only when none are passed (its doc for each method says
so). So anything a consumer sets on `DefaultEntryOptions` through `ConfigureFusionCache` - the
headline example in the XML docs and the guide is
`DefaultEntryOptions.AllowBackgroundBackplaneOperations = true` - has no effect on the SDK's reads,
writes or invalidations. Probed with `DefaultEntryOptions.Size = 1` over a size-limited memory cache
(probes 9 and 19): still threw. `FusionCacheOptions`-level settings (`CacheKeyPrefix`,
`BackplaneChannelPrefix`, `TagsDefaultEntryOptions`) do apply - 2.4's fix was probed through this
very hook - so the hook is not useless, only its documented example is.

**Fix.** After the callback runs, build the write options by duplicating
`fusionCacheOptions.DefaultEntryOptions` and then applying the SDK's non-negotiables on top (the
tier skips in memory mode, the rethrow flags, the duration and fail-safe policy from
`DeliveryCacheOptions`). Document which settings the SDK pins. A test: set
`DefaultEntryOptions.Size` through the hook and write into a size-limited cache.

### 2.8 Stored entries carry no format version - **medium, upgrade hazard**

The distributed tier stores `CacheEnvelope<CachedRawItemsPayload>` and the wire models, serialized
by the SDK's options, under keys that carry no format version. `ReThrowSerializationExceptions =
true` is deliberate for the write path, but it also governs reads: probed by writing an entry with
one payload type and reading the same key with a manager expecting another (probe 14, first pass) -
every read threw `FusionCacheSerializationException` instead of treating the entry as a miss. A
Redis outlives a deployment, so after an SDK release that changes `CachedRawItemsPayload`, a wire
model or the envelope, every cached key hit by the new version throws until the entry expires
(`DefaultExpiration`, one hour by default; up to `FailSafeMaxDuration` with fail-safe on).

Extended from the first pass: the same hazard exists one layer down. FusionCache's default
`DistributedCacheKeyModifierMode.Prefix` puts its wire-format version (`v2:`) into every
distributed key precisely so that a FusionCache upgrade that changes its entry format misses on old
entries rather than failing to read them. The SDK sets `None` for readable Redis keys
(`FusionCacheManager.cs:198`), so a future FusionCache major would read the old format through the
same `ReThrowSerializationExceptions = true`.

**Fix.** One version segment under the SDK's control in the hybrid key prefix - `v1:` between the
client prefix and the key, bumped whenever a cached type *or the FusionCache wire format* changes
shape (the approval-snapshot habit makes the first visible; the second is a dependency bump to look
at). With 2.4's move to `CacheKeyPrefix` the segment belongs in that prefix, so FusionCache's own
keys are versioned with it. Keep `ReThrowSerializationExceptions = true`; with versioned keys it
only ever reports a real bug. Note it in the release checklist, before 20.0.0 ships so the first
stable line starts at version 1.

### 2.9 Invalidation keys are case-sensitive while tracking is not - **low**

`DependencyTrackingContext` dedupes with `OrdinalIgnoreCase`, but tags are formatted verbatim and
FusionCache compares them ordinally. Probed (probe 7): `InvalidateAsync(["ITEM_HERO"])` left an
entry tagged `item_hero` in place; the exact key evicted it. The SDK's own keys are lower-case by
construction (codenames, `Guid` formatting), so this only affects consumer-built keys, but a webhook
handler that upper-cases or copies an id from a payload with different casing silently evicts
nothing. The root cause is that consumers build these strings by hand: `CacheDependencyKeyBuilder`,
which knows the formats, is internal.

**Fix.** Normalize to lower-case invariant in one place - the tag formatter and the start of
`InvalidateAsync` - and say so on `IDeliveryCacheManager.InvalidateAsync`. Better, make the key
builder public (`DeliveryCacheDependencies.ForItem(codename)` and friends next to the scope
constants), so the webhook handler in the guide composes keys the same way the SDK does and the
casing question does not arise.

### 2.10 A cold node pays one distributed read per tag on its first hit - **characteristic, document**

FusionCache verifies an entry's tags against tag-expiration entries on every read; those are cached
in the memory tier, but a node that has never seen them reads each from L2. Probed with an entry
carrying 50 tags (probe 13, first pass): the writing node did 1 L2 read; a fresh node's first hit
did 53; its second hit did 0. A listing of 100 items with their types, assets and taxonomies carries
several hundred tags, so the first hit on every node after a restart - and again after the tag
entries expire - costs that many Redis round trips. Not a bug, and the memory tier makes it a
warm-up cost, but the guide should say it, and `TagsDefaultEntryOptions.Duration` is the knob (set
it through `ConfigureFusionCache`, which does apply at that level).

### 2.11 Invalidated entries remain eligible for fail-safe - **behaviour, document**

`RemoveByTag` marks entries logically expired rather than deleting them when fail-safe was on at
write time. Probed (probe 11): after `InvalidateAsync(["item_x"])`, a factory that threw got the
pre-invalidation value served as a fail-safe hit, and so did a factory returning null. That is
FusionCache's intended semantics and the right call during an outage. It is the mechanism behind
2.1, and once 2.1 distinguishes outages from answers it is also the right behaviour after a webhook:
evicted, then served stale only while the origin is unreachable. The guide's purge section already
draws this distinction for `PurgeAsync(allowFailSafe: true)`; the invalidation section should draw
it too.

### 2.12 Smaller

- **The environment prefix is read once.** `EnvironmentIdOf` reads `EnvironmentId` when the manager
  is created and bakes it into the formatters. The DI path advertises per-request option reads, so a
  reload that switches environments keeps caching under the old prefix. Rare; a sentence in the
  guide's "changing options at runtime" note is enough (it already says to purge).
- **Hydrated entries are shared instances with the client's options baked in.** A memory hit hands
  back the very object the previous caller received - same `IContentItem<T>`, same `Elements`
  (probe 15) - with `CustomAssetDomain` and `DefaultRenditionPreset` already applied. A consumer who
  mutates a model mutates the cache for everyone, and a runtime change to either option does not
  reach cached entries until they expire or are purged. The hybrid path rehydrates per hit and has
  neither property. Both are inherent to caching hydrated objects and worth one paragraph in the
  guide; the first is the kind of thing a consumer discovers in production.
- **Typed dynamic models track fewer dependencies.** For `TModel` where `ModelTypeHelper.IsDynamic`
  is true, `ProcessItemsAsync` skips `CompleteItemAsync`, so element-level dependencies (assets,
  taxonomy groups, rich-text links) are not tracked; items, types and the list scope are. The
  untyped `GetItems()` bypasses the cache entirely, so this only reaches a caller that names a dynamic
  model type explicitly. Worth a line in the coverage section.
- **`ItemsListParams` with both `Elements` and `ExcludeElements`** puts only `Elements` in the key.
  If the API accepts both, two different projections share an entry; if the SDK forbids the
  combination, nothing to do.
- **Assets are missing from the invalidation matrix.** `asset_{guid}` is tracked from asset
  elements and rich-text images and listed in the `IDeliveryCacheManager` remarks, but the guide's
  "Invalidation Matrix" (`caching-guide.md:638`) has rows for items, types and taxonomies only, so
  an asset webhook has no documented mapping.
- **Guide drift.** "Keys have NO prefix: item:homepage" and "`EnvironmentId` ... [is] not part of
  query cache keys" (Key Prefixing, `:491-535`) predate `9558d0124`; every key now starts with the
  environment id, and the "No prefix (explicit)" example (`KeyPrefix = ""`) merges a named client
  into the default client's namespace on the same environment without saying so. The Redis
  troubleshooting entry (2.2) and the size-limit advice (2.3) are wrong in the direction that hurts.
  The `ConfigureFusionCache` example (2.7) is a no-op. The hybrid-cache note (`:103`) attributes
  per-hit rehydration to a FusionCache limitation; it follows from the SDK's own `RawJson` storage
  mode, and "negligible" undersells a JSON parse plus element mapping that includes the rich-text
  HTML parse. The "Monitor Cache Performance" and "Logging" wrappers (`:1106`, `:1279`) detect a miss
  with a local flag set inside the factory, which is exactly the pattern `CacheResult.FromFactory`
  exists to replace because eager refresh runs the factory for a different call; they should read
  `result.FromFactory`.

### 2.13 An invalidation is forgotten after thirty seconds unless the entry is read - **high, found during implementation**

Found while implementing 2.7, which is where the entry options are built. `RemoveByTag` and `Clear`
write a tag-expiration entry that every later read of a tagged entry checks against, and FusionCache
stores that entry with the options passed to the call. The SDK passed `_baseInvalidateOptions`, a
`FusionCacheEntryOptions` that named no `Duration`, so the tag data lived for FusionCache's entry
default of thirty seconds. An entry not read within thirty seconds of the webhook that invalidated it
was served afterwards for the rest of its own expiration, as if the webhook had never arrived.

Probed against FusionCache directly (probe 22): `RemoveByTag` with `Duration = 1s` on the options was
forgotten 1.5 seconds later; with `null` options, which selects `TagsDefaultEntryOptions` and its
ten-day default, it held. Then through the SDK (probe 23): cache the type listing, invalidate its list
scope, wait 32 seconds without reading, read - served from cache, origin never asked. A purge held in
the same probe, because `Clear` also keeps its timestamp in the instance; on a second node reading the
shared marker it would not.

None of the probes in either pass caught this because every one of them read the entry immediately
after invalidating it. The first pass's 2.10 even noted that tag entries expire without asking when.

**Fix.** Pass no options to `RemoveByTag` and `Clear`, so FusionCache uses `TagsDefaultEntryOptions`,
and configure that object with the SDK's pinned flags at construction; its duration stays FusionCache's
ten days and is the consumer's knob through `ConfigureFusionCache`. Document that it must exceed the
longest expiration in use. The test that pins it shortens the tag duration through the hook and shows
the invalidation lapsing with it, which is the only way to observe the lifetime without waiting.

## 3. Multi-client isolation, stated once

The namespace every key and tag lives in is `{KeyPrefix}:{EnvironmentId}:`, where `KeyPrefix`
defaults to the client name for a named client and to nothing for the default one
(`ResolveCacheKeyPrefix`). So the default client caches under `{env}:…`, a named client under
`{name}:{env}:…`, and hybrid keys add `cache:` / `dep:` after that. Everything else the key needs -
language, depth, projection, pagination, ordering, filters, and in memory mode the model type - is in
the key itself.

| Setup | Isolated? | By what |
|---|---|---|
| Two named clients, any environments, one `IMemoryCache` or one Redis | Keys and tags: yes | Client name in the prefix |
| Two applications, default clients, different environments, one Redis | Keys and tags: yes | Environment id in the prefix |
| Two applications, default clients, the *same* environment, one Redis | Shared, on purpose | Same content; hybrid stores raw JSON so each app rehydrates with its own asset options |
| Production and preview clients on one environment | Preview never caches | `GetEffectiveCacheManager` |
| Any of the above | **Purge: no** (2.4) | FusionCache's clear marker is outside the prefix |
| Default clients across applications, one Redis backplane | Notifications shared | `CacheName` lacks the environment id (2.4) |
| A named client with `KeyPrefix = ""` | Merged with the default client on that environment | The guide offers this without the consequence (2.12) |
| Two named clients given the same explicit `KeyPrefix` on one environment | Merged | Consumer's choice, but nothing warns |

Two things are global rather than per client and are fine as long as they are known: `ITypeProvider`
is one unkeyed registration shared by every client in a container, and the hybrid managers of one
container each open their own backplane instance (transient), so two hybrid clients mean two Redis
subscriptions unless the backplane is configured with a shared multiplexer.

Invalidation, checked against what a content change actually touches: an item webhook evicts the
item, every listing that contained it and every item whose modular content or rich text referenced
it; the list scope covers listings the new item should now appear in; a type webhook evicts the type
definition, type listings, and every item response containing an item of that type; a taxonomy
webhook evicts the group and every item whose taxonomy element draws from it; an asset webhook evicts
every item whose asset element or inline image is that asset. Components ride on their owning item.
Languages, used-in lookups, element lookups, the feed and the untyped queries are not cached, so
nothing to evict. The one hole is 2.1: with fail-safe on, an eviction followed by a definitive
answer does not stick.

## 4. What to simplify

The layer is not over-built for what it does; the two storage modes, the envelope, and the tag
plumbing each earn their place. What follows is where the same behaviour can be had with less, and
one place where the current shape is a liability.

### 4.1 Fail-safe classification: put it in the result, per call

`FusionCacheManager` subscribes to five FusionCache events (`FailSafeActivate`, `FactorySuccess`,
`Hit`, `Remove`, `Memory.Eviction`) to maintain `_failSafeActiveKeys`, a process-wide dictionary of
formatted keys with a 10,000-entry cap that is cleared wholesale when reached (`:37`, `:477-503`).
`CachedQueryExecutor` then asks the manager, through the internal `IFailSafeStateProvider`, whether
the key it just read is in that set (`CachedQueryExecutor.cs:70-71`), and the manager re-formats the
key to look it up (`:422-423`). This is a side channel answering a per-call question with global
state: two concurrent reads of one key, one served stale and one fresh, can classify each other, and
the cap is a "clear everything" bounded structure.

The events are needed - FusionCache does not return metadata from `GetOrSetAsync`, and the throttled
stale hit is only observable through `Hit` with `IsStale` - but the *state* does not have to be
global. With `EnableSyncEventHandlersExecution` the handlers run inline on the calling thread, so a
per-call slot (an `AsyncLocal` set around the `GetOrSetAsync` call, checked by the handlers for the
matching key) records "this call observed a stale hit or a fail-safe activation for this key" and
nothing else. The manager then returns it as `CacheResult<T>.IsStale` next to `FromFactory`, the
executor reads the result instead of probing, and `IFailSafeStateProvider`, the dictionary, the
cap, the unformatted-key removals (`:309`) and the double lookup all go. `CacheResult<T>` gains one
init-only property, which a custom manager is free to set.

### 4.2 The two managers are one manager

`MemoryCacheManager` and `HybridCacheManager` are 47 and 59 lines that forward every member to a
`FusionCacheManager` built by `CreateMemory` or `CreateHybrid`. They exist to give `UseMemoryCache`
and `UseHybridCache` a type to construct and the tests a type to assert on. Registering
`FusionCacheManager.CreateMemory(...)` and `CreateHybrid(...)` directly removes both classes and
changes nothing observable except the logger category, which moves from
`Kontent.Ai.Delivery.Caching.MemoryCacheManager` to `…FusionCacheManager` - acceptable in a
prerelease, and the category can be kept by naming the logger explicitly if it matters. The
`IDeliveryCacheManager`, `IDeliveryCachePurger` and `IDisposable` surface is unchanged.

### 4.3 Let FusionCache own the prefix

With 2.4's `CacheKeyPrefix`, `_cacheKeyFormatter` and `_dependencyTagFormatter` become two constant
strings, `ComposeKeyPrefix` moves into the options, and the `cache:` / `dep:` segments can go: they
exist to keep keys and tags apart in Redis, but FusionCache already stores tag data under
`__fc:t:{tag}`, so a key and a tag cannot collide whatever they are called. The version segment
from 2.8 takes their place. This changes Redis key shapes, which is one more reason to do it before
20.0.0 rather than after.

### 4.4 One cached-fetch helper for items and listings

`ItemQuery` and `ItemsQuery` each carry a raw-JSON branch and a hydrated branch
(`ItemQuery.cs:116-189`, `ItemsQuery.cs:161-234`) that differ only in the payload factory and the
rehydrator. The mode split itself should stay: hydrated objects cannot cross a process boundary,
and rehydrating on every memory hit would put a JSON parse, element mapping and an AngleSharp parse
of every rich-text element on the hot path of the fastest cache tier. But the two branches are one
generic method with two delegates, and `TypeQuery`, `TaxonomyQuery` and the listings already show
what the single-branch shape looks like.

### 4.5 Defensive code nothing reaches

- `GetOrSetAsync`'s empty-key path (`FusionCacheManager.cs:264-275`) runs the factory uncached.
  `CacheKeyBuilder` cannot produce an empty key and no SDK code passes one; the path is pinned by a
  unit test and reached by nothing else.
- Dependencies are deduplicated case-insensitively in `DependencyTrackingContext` and again in the
  manager (`:270-273`, `:301-304`), and the type and taxonomy listing builders dedupe a third time
  in their `BuildDependencies`. One place is enough; the manager is the boundary, so keep that one.
- `_failSafeActiveKeys.TryRemove(cacheKey, …)` on the unformatted key (`:309`) removes nothing that
  was ever added; it goes with 4.1.

### 4.6 Not to change

The envelope-identity `FromFactory` test, `EnableSyncEventHandlersExecution`, storing the
dependency keys inside the envelope so a hit can surface them, the model-type discriminator in
memory mode only, and the `Distinct`-then-`ConvertAll` tag shaping inside the factory are each the
plain answer to a real constraint, and the first pass's §3 found no defect in any of them.

## 5. What was looked for and not found

- Duplicate keys for one logical query: none. Order-independence holds for filters and projections;
  hydrated and raw-JSON modes are keyed differently on purpose; `WaitForLoadingNewContent` bypasses
  both lookup and store; the auto-added type filter for typed models is part of the hash consistently.
- Cross-client tag collisions: none, in memory or distributed, with or without a backplane, in
  either pass.
- Per-query expiration leaking across calls: none; the factory context's options are a duplicate
  (probe 14).
- Backplane misuse: none - transient instances, one `SetupBackplane` per cache, synchronous publish
  by default so an invalidation has reached the channel before `InvalidateAsync` returns.
- Disposal: the manager disposes its FusionCache; FusionCache disposes only what it created (not the
  application's memory cache, not the `IDistributedCache`); the container disposes the manager.

## 6. Suggested order

1. **2.1** first, on its own: it is the one finding where a consumer sees wrong content rather than
   a slow or failing call, and it decides the factory contract that 4.1 then builds on.
2. **2.2, 2.3 and 2.6** together - the first two turn infrastructure conditions into thrown
   exceptions in every cached query and are one flag each plus a test; 2.6 is what makes 2.2's
   swallowing observable, and 2.3's `Size` wants 2.7's options plumbing.
3. **2.7**, because it decides how 2.3's `Size` and the consumer's own settings flow into the
   SDK's entry options.
4. **2.4, 2.8 and 4.3** as one change to the key scheme: `CacheKeyPrefix` with the environment and
   a version segment in it, `CacheName` with the environment id, the `cache:`/`dep:` segments
   dropped, two-tenant purge tests over both stores with a fresh node. Before 20.0.0, since it
   changes Redis keys.
5. **2.5** - the unkeyed alias and the standalone client's handle; two registrations and a property.
6. **2.9** - normalization, and the public key builder.
7. **4.1, 4.2, 4.4, 4.5** - internal, no snapshot movement except `CacheResult<T>.IsStale`.
8. **Docs**: the guide's key-prefixing section, the Redis and memory-pressure troubleshooting
   entries, the escape-hatch example, the default-client and standalone invalidation routes, the
   asset row in the matrix, the cold-node cost, fail-safe after invalidation, the shared-instance
   and baked-options paragraph, dynamic models, and the two wrapper samples that should read
   `FromFactory`.

Everything is behind the existing public surface except the additions in 2.5, 2.9 and 4.1 and the
doc wording; the approval snapshots move only for those. Every item is a candidate for the release
candidate line rather than a later major: 2.1, 2.2, 2.3 and 2.4 are the kind of thing a first
production deployment finds.

## 7. Probes

The first pass ran each as a throwaway xUnit fact in `Kontent.Ai.Delivery.Tests` against the
internal managers; the second ran a throwaway console program against the built assemblies through
the public surface only. Both were deleted afterwards. Re-run any of them by rebuilding from its
description. Rows the second pass overturned are marked.

| # | Pass | Setup | Result |
|---|---|---|---|
| 1 | both | Two named clients over one `MemoryCache`; `a` purges | First pass: isolated. **Overturned** by the second: `b` went to the origin too (probe 1); a control run without the purge kept `b` on cache; with `CacheKeyPrefix` per client, `b` kept its entry (2.4) |
| 1b | second | Same, `a` invalidates `scope_types_list` | `a` refetched, `b` served from cache - tag invalidation isolated |
| 2 | both | Two clients over one `MemoryDistributedCache`; `a` purges; `b` read from its warm node and from a fresh node | First pass: warm serves, fresh misses. **Refined**: whichever of `b`'s nodes reads first after the purge misses and repopulates; the other then hits (2.4). Recorded L2 keys: `a:{env}:cache:types`, `__fc:t:{prefix}dep:…`, and a bare `__fc:t:!` |
| 2b | second | Probe 2 with the fresh node reading first | Fresh `b` missed, warm `b` then hit; with `CacheKeyPrefix` both hit, one origin call for `b` in total |
| 3 | both | Probe 2 with `FusionCacheOptions.CacheKeyPrefix` set per client | `b` unaffected in every ordering; `a`'s own fresh node sees purge and invalidation (fix for 2.4) |
| 4 | first | `AddFusionCacheMemoryBackplane`, resolved twice; one instance handed to two managers | Transient; both managers work; invalidation isolated |
| 5 | first | `AddFusionCacheStackExchangeRedisBackplane` descriptor lifetime | Transient |
| 6 | first | Hybrid manager over a throwing `IDistributedCache`, fail-safe off and on | Every `GetOrSetAsync` throws `FusionCacheDistributedCacheException`; `InvalidateAsync` returns true (2.2) |
| 7 | first | `InvalidateAsync(["ITEM_HERO"])` against a tag `item_hero` | Not evicted; exact case evicts (2.9) |
| 8 | first | Memory manager over `MemoryCache { SizeLimit = 10000 }` | Every write throws (2.3) |
| 9 | first | Same as 8 with `ConfigureFusionCache(f => f.DefaultEntryOptions.Size = 1)` | Still throws (2.7) |
| 10 | first | Manager disposed; application's `MemoryCache` used afterwards | Still usable |
| 11 | first | Fail-safe on; invalidate; factory throws / returns null | Pre-invalidation value served as fail-safe (2.11) |
| 12 | first | Two default clients on different environments, shared L2 and backplane channel | Invalidate isolated; purge leaks to the other (2.4) |
| 13 | second | Default client, `UseMemoryCache` with fail-safe on and zero throttle; item cached; `InvalidateAsync(["item_…"])`; origin then answers `404` | Every read: `IsSuccess = true`, `ResponseSource.FailSafe`, and the origin asked each time. Same after natural expiry. With fail-safe off: the `404` as a failed result (2.1) |
| 13b | first | Entry with 50 tags; counting `IDistributedCache`; fresh node reads twice | Write path 1 read; cold first hit 53; warm second hit 0 (2.10) |
| 14 | both | First pass: entry written as `CacheEnvelope<Payload>`, read as `CacheEnvelope<Other>`. Second pass: `_baseWriteOptions.Duration` before and after a `WithCacheExpiration` query | `FusionCacheSerializationException` on read (2.8). Duration unchanged at one hour: per-query expiration does not leak |
| 15 | second | Default client, memory: same item read twice; unkeyed `IDeliveryCacheManager` resolved | Same `IContentItem` and `Elements` instance on the hit; eight dependency keys for the fixture item; unkeyed manager resolves to nothing (2.5, 2.12) |
| 16 | second | Three concurrent `GetTypes()` on a cold memory cache, origin delayed | One origin call; sources `Origin`, `Cache`, `Cache` |
| 17 | second | Default client, hybrid over `MemoryDistributedCache`: same item read twice | Fresh instance on the hit, `ResponseSource.Cache` |
| 18 | second | Probe 6 through the public surface: `UseHybridCache` over a throwing `IDistributedCache`, `GetTypes().ExecuteAsync()` | The query itself throws `FusionCacheDistributedCacheException`, not a failed result; fail-safe on or off (2.2) |
| 19 | second | Probe 8 and 9 through the public surface: `AddMemoryCache(o => o.SizeLimit = 1000)` + `UseMemoryCache`, with and without `DefaultEntryOptions.Size` through the hook | Throws either way (2.3, 2.7) |
| 20 | second | Both backplane packages' `IFusionCacheBackplane` descriptor lifetime | Transient |
| 21 | second | FusionCache directly, fail-safe on, entry expired: (a) factory throws; (b) factory sets `ctx.Options.IsFailSafeEnabled = false` and throws; (c) next call with fail-safe on; (d) after `RemoveAsync`, factory throws; (e) after `ExpireAsync`, factory throws | (a) stale served; (b) exception propagates; (c) stale served again - the copy is still there; (d) exception propagates; (e) stale served. The mechanics 2.1's fix needs |
| 22 | implementation | FusionCache directly: `RemoveByTag` with options `Duration = 1s`, with a bare `FusionCacheEntryOptions`, and with `null`; entry read 1.5 s later | 1 s: served again; bare: gone at 1.5 s (but its duration is 30 s); null: gone. `FusionCacheEntryOptions.Duration` defaults to 30 s, `TagsDefaultEntryOptions.Duration` to 10 days (2.13) |
| 23 | implementation | Default client, `UseMemoryCache`: type listing cached, `scope_types_list` invalidated, 32 s without a read, then read; then purge, 32 s, read | Invalidation forgotten - served from cache, origin not asked. Purge held (2.13) |
