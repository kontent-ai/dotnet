# Collapsing Delivery's query builders

Plan for removing the duplication in `src/delivery/Kontent.Ai.Delivery/Api/QueryBuilders`, written
after tracing a request end to end to find out where the SDK's perceived complexity actually lives.

> [!NOTE]
> **Proposed, not scheduled.** The trigger was a suspicion that Delivery's internals are overly
> complicated — too many mappers, providers and services. That turned out to be wrong in its
> specifics and right in its conclusion: the service graph is lean, and the query builders are where
> the weight is. This document records the measurements so the question isn't re-litigated.

**Verdict: the services are not the problem. 2,472 lines of query builders are, and two ~225-line
files among them are 97% the same file. A shared helper layer already exists and covers the hard
part; what is still copied is the shell around it.**

---

## 1. What was measured

The full read path, from `DeliveryClient.GetItem<T>` to a mapped model:

```
DeliveryClient.GetItem<T>(codename)
  └─ new ItemQuery<T>(api, codename, mapper, deserializer, cache, preset, domain, logger)
       ├─ IDeliveryApi (Refit)  →  RefitApiResponseExtensions  →  IDeliveryResult<T>
       ├─ IContentDeserializer  →  ContentItem<T> from raw JSON
       └─ ContentItemMapper
            ├─ IItemTypingStrategy  →  codename → CLR type
            ├─ PropertyMappingInfo  →  cached reflection per property
            ├─ ElementValueMapper   →  per-element-kind hydration
            │    └─ IContentDependencyExtractor → cache invalidation keys
            └─ LinkedItemResolver   →  recursive, cycle-safe
```

## 2. The service graph is lean — leave it alone

`RegisterDependencies` holds **nine registrations**. Each was checked for the failure mode worth
suspecting — a layer that only forwards — and none of them does:

| Type | Earns its place because |
|---|---|
| `ContentDeserializer` | caches `MakeGenericType` results; two input forms (string, `JsonElement`) |
| `DefaultItemTypingStrategy` | memoizes codename→type, falls back to `DynamicElements`, logs the fallback |
| `ContentItemMapper` | caches reflected property maps per model type; orchestrates hydration |
| `ElementValueMapper` | per-element-kind mapping — the largest genuine unit of work at 354 lines |
| `LinkedItemResolver` | recursive resolution with cycle safety |
| `PropertyMappingInfo` / `MappingContext` | cached reflection metadata; a parameter object that stops five arguments threading through the recursion |
| `ContentDependencyExtractor` | derives cache-invalidation keys from rich text and taxonomy elements |

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
types side, and the entity type in `BuildDependencies`.

The skeleton repeats beyond those pairs: `FetchFromApiAsync` in 8 files, `WrapSuccess` in 8,
`ExecuteWithoutCacheAsync` in 7, `CreateFailureResult` in 6. Every caching query hand-writes three
execution paths — RawJson cache, hydrated cache, no cache.

The cost is not the line count. It is that adding an entity type means copying a 225-line file, and
that any fix to cache execution has to be found and applied in six places — the same shape of
hazard the pre-GA review flagged as "one skeleton duplicated across six query builders."

## 4. Two findings that fell out of the trace

### 4.1 The dynamic path has no caching, silently

`ItemQuery` references the cache manager 13 times; `DynamicItemQuery` once. `DeliveryClient` passes
a cache manager to `GetItem<T>(codename)` and **not** to `GetItem(codename)`. A consumer who calls
`AddDeliveryCache(...)` and then uses dynamic queries gets nothing, with no error and no log line.

This also explains a measurement that first looked like an anomaly: typed and dynamic query pairs
overlap only 25–39%, because the dynamic files are not copies — they are a feature-reduced variant
(93 lines vs 279).

Decide whether this is a limitation to document or a gap to close. It is currently neither.

### 4.2 Two public interfaces are redundant seams

- **`IItemTypingStrategy`** — public, one implementation, never overridden anywhere including tests,
  and it answers the same question as `ITypeProvider` ("codename → CLR type") one layer up.
  `ITypeProvider` is the real extension point: it is public, documented, and what the source
  generator emits against. The *class* earns its keep; the public interface is a second seam for one
  concern.
- **`IContentDeserializer`** — public, but its contract is welded to the internal `ContentItem<T>`
  shape (`object DeserializeContentItem(string, Type)`). It invites replacing something that cannot
  meaningfully be replaced.

Both freeze at GA. Making them internal is the cheap direction and can't be taken later.

(`IContentDependencyExtractor` is single-implementation too, but internal, so it costs nothing
publicly. Fold it into a static class only if the file is being touched anyway.)

## 5. Proposed design — finish an extraction that is already half done

`Api/QueryBuilders/Helpers/` already holds 160 lines of shared machinery, and it covers the subtle
part:

| Helper | Owns |
|---|---|
| `CachedQueryExecutor` | cache-hit vs fail-safe vs fetched classification, including the `FromFactory` check that makes eager refresh safe |
| `QueryLoggingHelper` | the start/complete/failed logging shape |
| `QueryExecutionResultHelper` | the "API result must exist by here" assertion |
| `OffsetPaginationHelper` | skip/limit arithmetic |

So the cache orchestration is **not** what is duplicated. What is copied across every query is the
**shell around it**: `FetchFromApiAsync`, `WrapSuccess`, `CreateFailureResult`, `BuildDependencies`,
`ExecuteWithoutCacheAsync`, cache-key construction, and the next-page fetcher. That shell is what
the 96.9% figure is measuring.

**Not a generic base class.** A `ListingQuery<TParams, TResponse, TPublicResponse, TEntity>` needs
four type parameters and forces inheritance on types whose public fluent surface differs.

**Extend `Helpers/` with the shell**, so a query keeps its fluent setters and its public interface
and `ExecuteAsync` becomes one call — the entity-specific parts passed in:

```csharp
// sketch — the cached path already routes through CachedQueryExecutor internally
internal static Task<IDeliveryResult<TPublic>> RunAsync<TResponse, TPublic>(
    Func<CancellationToken, Task<IDeliveryResult<TResponse>>> fetch,
    Func<TResponse, string[]> dependencies,
    string cacheKey,
    IDeliveryCacheManager? cacheManager,
    QueryLoggingHelper log,
    CancellationToken cancellationToken)
    where TResponse : TPublic;
```

Expected shape after: the three listing queries (559 lines) around 200; the two single-entity
queries (269 lines) around 120; `ItemQuery`/`ItemsQuery` keep their extra hydration logic but shed
the shell.

## 6. Sequencing

1. **Add the shell helper to `Helpers/`**, migrate `LanguagesQuery` first — it is the smallest and has no
   RawJson path, so it exercises the plain route.
2. **Migrate `TypesQuery` + `TaxonomiesQuery`** — the 97% pair, where the payoff is largest.
3. **Migrate `TypeQuery` + `TaxonomyQuery`.**
4. **Migrate `ItemQuery` / `ItemsQuery`** — last, because they add the RawJson/hydrated split.
5. **Decide 4.1 and 4.2** — independent of the above, and 4.2 is the only part with a GA deadline.

Steps 1–4 are internal only; the public query interfaces do not move, so the approval snapshots
should not change. That is the check that the refactor stayed honest.

## 7. Open questions

1. **Is 4.1 a bug or a documented limitation?** Caching dynamic queries needs a cache key that does
   not depend on a model type. Worth confirming the constraint before deciding.
2. **Does `WithElements` belong on the shared shape?** It exists only on `TypesQuery` today. If the
   API supports it for taxonomies, its absence there is a gap rather than a difference.
3. **How far to take it?** Steps 1–3 are clear wins. Step 4 touches the most-used path in the SDK
   and carries the most risk; it is reasonable to stop after step 3 and reassess.

## 8. Not examined

Rich-text resolution (`ContentItems/RichText`, 9 files) and the internals of
`Kontent.Ai.Delivery.Caching` were out of scope for this trace. Findings there would be additional,
not contradictory.
