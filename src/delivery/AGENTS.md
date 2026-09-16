# AGENTS.md

Guidance for coding agents working on the **Delivery SDK**. The root `AGENTS.md` covers everything repo-wide; this file holds only what is specific to this product.

## Overview

A client for the Delivery API — a *read* API. Five packages ship in lockstep on `<DeliveryVersion>`: `Kontent.Ai.Delivery`, `.Abstractions` (the public contract surface, no package references), `.Caching` (FusionCache-backed cache manager), `.SourceGeneration` (a Roslyn generator, `netstandard2.0`), and `Kontent.Ai.Urls` (image URL builder, standalone, no Delivery reference).

**`docs/for-developers.md` is the architecture map** — request shape, registration, transport, queries, results, deserialization and hydration, rich text, caching, logging, tests. Read it before changing anything below the public surface; do not restate it here. The load-bearing invariants it names:

- **Two hosting modes over one builder**, `services.AddDeliveryClient(delivery => …)` and `DeliveryClient.Create(delivery => …)`; the builder is `IDeliveryClientBuilder` (`Name`, `Services`, `Options`, `HttpClient`, `TuneRetry`, `ConfigureResilience`) over `src/common/Clients`. The default client's unnamed `IOptions<DeliveryOptions>` is a copy that follows the named options; `services.Configure<DeliveryOptions>()` before `AddDeliveryClient` is overwritten.
- **Refit transport** (`Api/IDeliveryApi.*.cs`, one method per endpoint) under resilience → tracking → authentication → `FilterQueryHandler`. Filters are an ordered list rendered by a handler, not a Refit dictionary, because the same key can appear more than once.
- **`System.Text.Json` only**, through the private `DeliveryJsonOptions` service so an application's own `JsonSerializerOptions` cannot displace the converters. `ContentItemConverterFactory` keeps the raw item JSON; everything after deserialization (dependency keys, hydration, linked items) reads that raw JSON, not the model.
- **Typed models resolve through source generation.** `Kontent.Ai.Delivery.SourceGeneration` emits `ContentTypeCodenameAttribute` as `internal` into each consuming compilation and a `GeneratedTypeProvider`; the runtime `TypeProvider` only discovers it (bounded assembly walk, `Lazy<T>`, catches broadly because a `Lazy<T>` caches a thrown exception). A user `ITypeProvider` is registered with `AddSingleton`; `TryAddSingleton` after `AddDeliveryClient` silently loses.
- **Hydration**: allocate, register, then populate — an item enters `MappingContext.ItemsBeingHydrated` before its elements are mapped, so cycles share the instance. Memoization is per root, not per response. Value-type models are refused (also generator diagnostic `KDSG003`).
- **Rich text** is parsed once by AngleSharp into an SDK-owned block tree; the public model never exposes AngleSharp. A content-item link has no default resolver — its URL is the application's to decide.
- **Result pattern**: `IDeliveryResult<T>` everywhere, with `ResponseSource` and `DependencyKeys`. The one exception is feed and used-in enumeration, which throws `DeliveryRequestException` on a failed page because a walk has no single result to carry a failure in.

## Current phase

`eng/Versions.props` is the authority. The `20.x` line moves to `net10.0` and rewrites registration onto one builder; `docs/upgrade/19-to-20.md` is the guide in progress and `18-to-19.md` is frozen. The `CHANGELOG.md` `## Unreleased` section already carries the stable-major overview; keep adding entries under the headings below it.

## Divergences from the siblings

Delivery and Sync are the read-only pair and compile the same `src/common/Http/DefaultResilience.cs` and `HttpClientTimeouts.cs`: retry on anything transient, `Retry-After` honoured, a 30-second per-attempt timeout inside the retry, and `HttpClient.Timeout` set to infinite when the SDK's own pipeline is in use. Management deliberately does neither; do not "align" the two directions.

- `Api/Filtering/FilterPath.cs` lower-cases filter keys so two spellings of one query share a cache entry. This is a read-side normalization and must not be copied to Management.
- Delivery has an Abstractions package and a public interface per model; Management considered and dropped both. Neither side is the template for the other.

## Model and query conventions

- Abstractions declares the `I*` contract, Delivery implements it as a `public sealed record` with `/// <inheritdoc/>`, and the SDK returns the interface. Every public type in the Abstractions assembly must live under `Kontent.Ai.Delivery.Abstractions` (test-enforced).
- `init`-only, `required` where the API always sends the value, explicit `[JsonPropertyName]` on every property. Collections are `IEnumerable`/`IReadOnlyDictionary`.
- **Dates are `DateTime` throughout**, including the filter overloads, and `IDateTimeContent.DisplayTimezone` carries the IANA name separately. A response timestamp reads as `DateTime` and passes straight into a filter that takes one; see the date rule in the root `AGENTS.md`.
- Query builders: `With*` for shaping, bare verbs for paging, `Where(f => f.System("type").IsEqualTo(...))` with AND semantics, terminal `ExecuteAsync`/`EnumerateAsync`. Client methods return a builder, never a `Task`. The DSL takes raw values; a caller who pre-encodes gets their percent signs.
- Generated content models need both `[ContentTypeCodename]` and `[JsonPropertyName]`; a property without the latter is dropped silently with `IsSuccess == true`.
- `DeliveryOptions.CopyTo` is reflection on purpose: a hand-written list keeps compiling when an option is added and silently stops carrying it.

## Caching — decided

- `IDeliveryCacheManager.GetOrSetAsync` factory protocol: `null` means the origin has no value (nothing cached, stale copy dropped); a throw means the origin is unreachable (fail-safe may serve stale). Do not blur the two.
- Cached families are item, items, type, types, taxonomy, taxonomies. Languages, single elements, used-in and all dynamic queries always reach the API; preview, `WaitForLoadingNewContent(true)` and dynamic results are never cached. The preview bypass lives in `DeliveryClient`, not in the cache manager.
- Keys are built by `Caching/CacheKeyBuilder`: readable, deterministic, order-independent, filters hashed, model type appended only in hydrated-object mode, credentials never part of identity. `FusionCacheManager` folds the environment id into the prefix and the backplane channel name, and `DistributedFormatVersion` is bumped whenever a cached type or FusionCache's entry format changes.
- Tag data outlives entries; tag reads fail open, invalidations do not and report `false`. `PurgeAsync` reports the same way — `false` when a distributed purge cannot complete — and only cancellation and disposal throw.
- Asset elements are not tagged, because the URL GUID identifies the binary, not the asset; `InvalidateAssetAsync` is the route for asset events.
- `ConfigureFusionCache` may not override what `DeliveryCacheOptions` decides: duration, fail-safe, jitter, eager refresh.
- `Kontent.Ai.Delivery.Caching` compiles no `src/common` files, knowingly, because a second copy would be `CS0436`-ambiguous through the `InternalsVisibleTo` chain.

## Source generator

The Roslyn package version in `Kontent.Ai.Delivery.SourceGeneration.csproj` bypasses Central Package Management on purpose: it decides the oldest compiler that can load the generator. Raise it only for a newer Roslyn API and treat that as a consumer-visible change. Diagnostics are `KDSG001`–`KDSG003`. The package is consumed as an analyzer and is not swapped to a ProjectReference in `/p:UseProjectReferences=true` mode.

## Testing conventions

- Four test projects, one per shipped assembly except Caching, which is tested and approval-gated from `Kontent.Ai.Delivery.Tests` (two Verify snapshots side by side under `ApiApproval/`).
- No shared base class. A test builds its own client inline: `MockHttpMessageHandler`, fresh `Guid` environment id, `AddDeliveryClient` with the mock as primary handler, resolve `IDeliveryClient` — so the real Refit and handler chain runs. `DeliveryClientTests.cs` is the canonical shape.
- Fixtures are recorded API responses under `Fixtures/`; test models under `Models/ContentTypes/`.
- The `src/common` tests compile into `Kontent.Ai.Delivery.Tests` through `$(KontentTestingPath)`, so the same assertions run against this SDK's copy.
- Coverage thresholds are per project and each csproj states the measurement date and reasoning; an Abstractions-only test belongs in the Abstractions test project or the threshold moves by whole points.
- Redis tests are opt-in via `KONTENT_SDK_RUN_REDIS_TESTS=true`; CI runs them in their own job against a service container.
- `Kontent.Ai.Delivery.Benchmarks` measures deserialization and hydration over fixtures, not the network; not packed, not run by CI.

## Docs

`README.md` is a routing page; the guides under `docs/` own the detail and the README links them. `scripts/lint-release-docs.sh` greps the docs for banned prerelease markers and is run by hand before a release, not by CI.
