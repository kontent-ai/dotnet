# Collapsing Delivery's query builders

Plan for removing the duplication in `src/delivery/Kontent.Ai.Delivery/Api/QueryBuilders`, written
after tracing a request end to end to find out where the SDK's perceived complexity actually lives.

> [!NOTE]
> **Parked, with §4.2 delivered.** The trigger was a suspicion that Delivery's internals are overly
> complicated — too many mappers, providers and services. That turned out to be wrong in its
> specifics and right in its conclusion: the service graph is lean, and the query builders are where
> the weight is. This document records the measurements so the question isn't re-litigated.
>
> **Status:** §4.2 shipped (`6bc3ea565`, `65dfd4b3b`, `89b1739e7`). §5 has been re-scoped after review
> — the original design sketch did not typecheck, and the ambition has been cut roughly in half; see
> §5. §4.1 is diagnosed but undecided. Nothing in §5/§6 has been implemented.

**Verdict: the services are not the problem. 2,472 lines of query builders are, and two ~225-line
files among them are 97% the same file. A shared helper layer already exists and covers the hard
part. What is worth extracting is one ~30-line block repeated six times — not the whole shell.**

---

## 1. What was measured

The full read path, from `DeliveryClient.GetItem<T>` to a mapped model:

```
DeliveryClient.GetItem<T>(codename)
  └─ new ItemQuery<T>(api, codename, mapper, deserializer, cache, preset, domain, logger)
       ├─ IDeliveryApi (Refit)  →  RefitApiResponseExtensions  →  IDeliveryResult<T>
       ├─ ContentDeserializer   →  ContentItem<T> from raw JSON
       └─ ContentItemMapper
            ├─ ItemTypingStrategy   →  codename → CLR type
            ├─ PropertyMappingInfo  →  cached reflection per property
            ├─ ElementValueMapper   →  per-element-kind hydration
            │    └─ ContentDependencyExtractor → cache invalidation keys
            └─ LinkedItemResolver   →  recursive, cycle-safe
```

## 2. The service graph is lean — leave it alone

`RegisterDependencies` holds **eight registrations** (nine before §4.2 removed one). Each was checked
for the failure mode worth suspecting — a layer that only forwards — and none of them does:

| Type | Earns its place because |
|---|---|
| `ContentDeserializer` | caches `MakeGenericType` for the runtime-typed path; two call shapes (static `TModel`, runtime `Type`) |
| `ItemTypingStrategy` | memoizes codename→type, falls back to `DynamicElements`, logs the fallback |
| `ContentItemMapper` | caches reflected property maps per model type; orchestrates hydration |
| `ElementValueMapper` | per-element-kind mapping — the largest genuine unit of work at 354 lines |
| `LinkedItemResolver` | recursive resolution with cycle safety |
| `PropertyMappingInfo` / `MappingContext` | cached reflection metadata; a parameter object that stops five arguments threading through the recursion |
| `ContentDependencyExtractor` | derives cache-invalidation keys from rich text and taxonomy elements (now a static class, so no longer registered) |

This is not the model generator's `GetElementType`, which returned its own argument. Merging
`ElementValueMapper` and `LinkedItemResolver` would produce one ~800-line class with recursive
cycle-tracking tangled into element dispatch — worse, not simpler.

**Conclusion: no work proposed here.**

## 3. The query builders are the weight

2,472 lines across 19 files, of which 160 lines are already a shared `Helpers/` layer — see §5.
Normalising only the entity name:

| Pair | Identical | Sizes |
|---|---|---|
| `TypesQuery` vs `TaxonomiesQuery` | **96.9%** | 229 / 223 |
| `TypeQuery` vs `TaxonomyQuery` | **84.8%** | 138 / 131 |

The *entire* difference between the first pair is four things: one namespace import, the params
record (`ListTypesParams` vs `ListTaxonomyGroupsParams`), one extra `WithElements` method on the
types side, and the entity type in `BuildDependencies`. Re-verified by normalised diff: **10
differing lines out of 229.**

The skeleton repeats beyond those pairs: `FetchFromApiAsync` in 8 files, `WrapSuccess` in 8,
`ExecuteWithoutCacheAsync` in 7, `CreateFailureResult` in 6. Every caching query hand-writes three
execution paths — RawJson cache, hydrated cache, no cache.

The cost is not the line count. It is that adding an entity type means copying a 225-line file, and
that any fix to cache execution has to be found and applied in six places — the same shape of
hazard the pre-GA review flagged as "one skeleton duplicated across six query builders."

## 4. Two findings that fell out of the trace

### 4.1 The dynamic path has no caching — and the stated reason is not the real one

`DeliveryClient` passes a cache manager to `GetItem<T>(codename)` (`DeliveryClient.cs:64`) and **not**
to `GetItem(codename)` (`:79`). The same split applies to `GetItems<T>()` vs `GetItems()`. A consumer
who calls `AddDeliveryCache(...)` and then uses dynamic queries gets nothing.

Two corrections to the original write-up:

- **It is documented, just not anywhere a consumer can see it.** Both `DynamicItemQuery` and
  `DynamicItemsQuery` carry an explicit `<remarks>` saying cache support is intentionally omitted.
  They are internal classes, so those docs reach no one.
- **The stated rationale — "the runtime-typed result type varies per item" — is not the blocker.**
  The inner `ItemQuery<IDynamicElements>` caches perfectly well, and runtime typing happens *after*
  the cache. The actual blocker is `ItemQuery.LatestModularContent`: it is assigned only in
  `ProcessItemAsync` (`ItemQuery.cs:233`) and reset to null on entry (`:62`), and `DynamicItemQuery`
  reads it (`:77`) to drive `TryRuntimeTypeItemAsync`. On a cache hit it would be null, so runtime
  typing would be **silently skipped** — dynamic items would come back typed on a miss and untyped on
  a hit. Enabling caching naively is a correctness trap, not merely "complex".

Closing it properly means caching modular content alongside the item. That is a real feature, not a
one-line wiring fix.

The gap is also wider than the dynamic queries: `GetLanguages()`, `GetContentElement()` and both
`GetItemsFeed()` overloads take no cache manager either. Languages is the one with no defence at all
— `CacheKeyBuilder` has no `BuildLanguagesKey`; it was never built.

### 4.2 Two public interfaces were redundant seams — **done**

Shipped across three commits.

- **`IItemTypingStrategy`** — was public with one implementation. Substitutable in principle (it only
  maps a codename to a `Type`), but it duplicates `ITypeProvider` one layer down, and overriding it
  silently forfeited memoization, the `DynamicElements` fallback and the fallback log line. Made
  internal, then folded into the class, which is now `ItemTypingStrategy` (the `Default` prefix only
  meant "default implementation of the interface").
- **`IContentDeserializer`** — was public and **could not be implemented at all**:
  `CachePayloadHelper` hard-cast the `object` return to the internal sealed `ContentItem<TModel>`, so
  a custom implementation limped along on the uncached routes and threw `InvalidCastException` the
  moment `CacheStorageMode.RawJson` rehydrated. Removed outright. `ContentDeserializer` now offers
  `Deserialize<TModel>(JsonElement)` for callers that know the model type at compile time (the cache
  path — both casts and the reflection behind them are gone) and `Deserialize(JsonElement, Type)`
  returning `IContentItem` for callers that resolve the type from a codename at runtime. The unused
  `string` overload and its default interface method went too.
- **`IContentDependencyExtractor`** — internal already, so it cost nothing publicly, but once the
  other two went the analyzer flagged (`S2325`) that both its methods could be static. It is now an
  `internal static class`, which also removed a DI registration and two constructor parameters.

Public API snapshots confirmed the first commit's diff was exactly the two interface blocks and the
later two changed no public surface at all.

## 5. Proposed design — re-scoped

`Api/QueryBuilders/Helpers/` already holds 160 lines of shared machinery, and it covers the subtle
part:

| Helper | Owns |
|---|---|
| `CachedQueryExecutor` | cache-hit vs fail-safe vs fetched classification, including the `FromFactory` check that makes eager refresh safe |
| `QueryLoggingHelper` | the start/complete/failed logging shape |
| `QueryExecutionResultHelper` | the "API result must exist by here" assertion |
| `OffsetPaginationHelper` | skip/limit arithmetic |

So cache orchestration is **not** what is duplicated. Two separable things are, with very different
value:

**(A) The cache-outcome interpretation — extract this.** ~30 lines repeated verbatim in all six
caching queries, encoding subtle semantics: the fail-safe probe, `cached?.Value ?? apiResult.Value`,
`cached?.DependencyKeys ?? Build(...)`. Six copies of that is the actual hazard, and it is the code
commit `1215ad1f4` already had to fix once.

**(B) The rest of the file shell — do not extract.** Fluent setters, `FetchFromApiAsync`,
`BuildDependencies`, `WithNextPageFetcher`, `CreateNextPageQuery`. Boring duplication: each copy is
verifiable by eye and drift is caught by the tests. Collapsing it needs a four-type-parameter core
plus ~5 constructor delegates to save ~150 lines nobody misreads. That trades clarity for line count,
against the repo's KISS rule.

### The original sketch was wrong

The first version of this document proposed:

```csharp
internal static Task<IDeliveryResult<TPublic>> RunAsync<TResponse, TPublic>(...)
    where TResponse : TPublic;
```

That constraint does not hold for `ItemQuery`, where the API type is `DeliveryItemResponse<TModel>`
and the public type is `IContentItem<TModel>` — unrelated types. It also omitted `TCached` (distinct
from both in `TypeQuery`: cached `ContentType`, public `IContentType`), the projection applied before
every exit, the storage-mode split, and the failure conversion. Written honestly it is 3 type
parameters, 4 delegates and 4 scalars — nine arguments, no better than the code it replaces.

### What to build instead

Return a classified outcome and let each query spend four lines on its own exits. `TPublic` never
enters the helper, so the variance problem disappears — 2 type parameters, 2 delegates:

```csharp
internal enum ResolvedQueryKind { CacheHit, FailSafeHit, Fetched, Failed }

internal readonly record struct ResolvedQuery<TCached, TApi>(
    ResolvedQueryKind Kind,
    TCached? Value,
    IReadOnlyList<string>? DependencyKeys,
    IDeliveryResult<TApi> ApiResult);
```

The helper absorbs the three `LogQueryCompleted` calls, `LogQueryFailed`, `EnsureApiResult` and both
`??` merges. Each call site becomes:

```csharp
var r = await CachedQueryExecutor.RunAsync(..., select: static a => a.Item, dependencies: BuildDependencies);
return r.Kind switch
{
    ResolvedQueryKind.Failed      => CreateFailureResult(r.ApiResult),
    ResolvedQueryKind.CacheHit    => DeliveryResult.CacheHit<TPublic>(WithNextPageFetcher(r.Value!), r.DependencyKeys),
    ResolvedQueryKind.FailSafeHit => DeliveryResult.FailSafeHit<TPublic>(WithNextPageFetcher(r.Value!), r.DependencyKeys),
    _                             => WrapSuccess(WithNextPageFetcher(r.Value!), r.ApiResult, r.DependencyKeys),
};
```

The four cases become *named* rather than buried in nested conditionals — a readability gain on top
of the dedup. Expect roughly 130 lines removed across six files, not the ~350 the first draft
projected.

**Precedent already in this folder.** `UsedInQueries.cs` runs two thin fluent facades over a shared
`UsedInQueryCore` that takes its varying piece as a `Func` in the constructor — composition, not a
base class, already tested. `DynamicItemQuery`/`DynamicItemsQuery` wrapping a typed inner query is a
second instance of the same instinct. Whatever gets built should look like those.

## 6. Sequencing

1. **Add `ResolvedQuery` + `RunAsync` to `Helpers/`; migrate `TypeQuery` and `TaxonomyQuery`** — the
   smallest cached shape, identity projection. (The first draft started with `LanguagesQuery`, but
   that query has no cache manager at all, so it exercises nothing the helper does.)
2. **Migrate `TypesQuery` + `TaxonomiesQuery`** — adds the projection.
3. **Migrate `ItemQuery` + `ItemsQuery`** — adds the storage-mode split and the `select` delegate.
   Gate on 1–2 landing clean; this is the SDK's hottest path.

All three are internal only; the public query interfaces do not move, so the approval snapshots must
come back byte-identical. That is the check that the refactor stayed honest.

## 7. Open questions

1. **Is 4.1 a bug or a documented limitation?** Now diagnosed (see §4.1): closing it requires caching
   modular content alongside the item so runtime typing survives a cache hit. Still undecided, and
   the `GetLanguages()` gap is the more clear-cut one.
2. ~~**Does `WithElements` belong on the shared shape?**~~ **Answered: no.** `ListTaxonomyGroupsParams`
   has only `Skip`/`Limit`; elements are a content-type concept and taxonomies have no element
   surface. A genuine difference, not a gap.
3. ~~**How far to take it?**~~ **Answered by the (A)/(B) split above.** Steps 1–2 are clear wins,
   step 3 is the one to reassess.

## 8. Not examined

Rich-text resolution (`ContentItems/RichText`, 9 files) and the internals of
`Kontent.Ai.Delivery.Caching` were out of scope for this trace. Findings there would be additional,
not contradictory.

The deserialization path *was* subsequently examined and reworked — see §4.2. That work also turned
up drift in `src/delivery/docs/for-developers.md`, whose `ContentDeserializer` snippet described
compiled-delegate caches the class has never had. Only the sections touched by §4.2 were corrected;
the rest of that document has not been audited.
