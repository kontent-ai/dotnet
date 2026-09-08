# For developers: how the Delivery SDK is put together

This is the map for someone changing the SDK, not using it. It names the boundaries, says what each
one guarantees, and points at the file where the guarantee lives. Consumer-facing behaviour is in the
[README](../README.md); caching in depth is in the [caching guide](caching-guide.md); rich text
rendering in the [rich text guide](rich-text-customization.md).

Paths below are relative to `src/delivery/`. Where a file is shared with the other SDKs it lives under
`src/common/` and is compiled into this assembly; see [that README](../../common/README.md) for why
it is source rather than a package.

## The shape of a request

```text
IDeliveryClient.GetItem<T>(codename)            DeliveryClient.cs
  -> ItemQuery<T>                               Api/QueryBuilders/ItemQuery.cs
     -> cache lookup (if a manager is attached)  Api/QueryBuilders/Helpers/CachedItemsFetch.cs
        miss -> IDeliveryApi (Refit)             Api/IDeliveryApi.*.cs
                -> handler chain                 Handlers/, src/common/Http/
                -> converters keep the raw JSON  Serialization/Converters/
             -> dependency keys from that JSON   ContentItems/Processing/ResponseDependencyExtractor.cs
             -> hydrate elements onto the model  ContentItems/Mapping/
             -> store (hydrated or raw)          Kontent.Ai.Delivery.Caching
  -> IDeliveryResult<T>                          Abstractions/SharedModels/DeliveryResult.cs
```

Every layer is internal except the two ends: `IDeliveryClient` and the query interfaces at the top,
`IDeliveryResult<T>` and the models at the bottom. The public API is gated per package by an approval
snapshot in each test project's `ApiApproval` folder; a change to it fails the build until the
`.received.txt` is reviewed and accepted.

## Registration

`AddDeliveryClient` and `DeliveryClient.Create` run the same registration
(`Extensions/ServiceCollectionExtensions.cs`). A client is a *name*: its options, HTTP client, Refit
client, cache manager and the client itself are keyed services under that name, and the default
client's are also registered unkeyed so an application that asks for `IDeliveryClient` or
`IDeliveryCacheManager` gets it without knowing the key. The sequence - duplicate check, validated
options, one Refit client per transport with its resilience and handlers, then the client and its
factory - is `src/common/Clients/ClientRegistration.cs`, shared with the Management and Sync SDKs.

`DeliveryClient.Create` runs it over a private container the client owns. That is why a standalone
client behaves like a container-resolved one: same handler chain, same connection recycling, same
options copy.

The SDK's own dependencies (`ServiceCollectionExtensions.Dependencies.cs`) are `TryAdd`ed once and
shared by every client: the JSON options under a private wrapper type (`Configuration/DeliveryJsonOptions.cs`,
so an application registering `JsonSerializerOptions` for itself cannot replace the SDK's), the type
provider, the deserializer, the element mapper and the item mapper.

## Transport

`IDeliveryApi` is a Refit interface split by endpoint family (`Api/IDeliveryApi.Items.cs` and
siblings), source-generated, one method per endpoint returning `IApiResponse<T>`. The handler chain
around it, outermost first:

1. **Resilience** - `src/common/Http/DefaultResilience.cs`: retry on the shared predicate
   (`HttpRetryPredicates.cs`), `Retry-After` honoured (`HttpRetryDelay.cs`), a per-attempt timeout and a
   total timeout from `DeliveryOptions.Timeout` (`HttpClientTimeouts.cs`). Replaced wholesale by
   `ConfigureResilience` on the builder.
2. **Tracking** - `Handlers/TrackingHandler.cs`: `X-KC-SDKID`, and `X-KC-SOURCE` from the calling
   assembly's `DeliverySourceTrackingHeader` attribute.
3. **Authentication and endpoint** - `Handlers/DeliveryAuthenticationHandler.cs`: the API key for the
   preview or secure endpoint, the environment id in the path.
4. **Filters** - `Handlers/FilterQueryHandler.cs`: appends the query string rendered from the ordered
   filter list (`Api/Filtering/FilterQueryString.cs`). Filters are kept in order and rendered by a
   handler rather than passed to Refit as a dictionary, because the same key can appear more than once.

An `IApiResponse<T>` becomes an `IDeliveryResult<T>` in `Extensions/RefitApiResponseExtensions.cs`:
status, headers, `X-Stale-Content`, and an `IError` parsed from the body by
`src/common/Http/RefitErrorParsing.cs`, with the exception on `Error.Exception` when the failure was
a transport one.

## Queries

One class per endpoint under `Api/QueryBuilders/`. A query holds its parameters as an immutable
params record (`Api/QueryParams/`) plus the ordered filter list, so `with` expressions and a snapshot
for the next-page fetcher are cheap. `ExecuteAsync` decides between the cached and uncached path;
nothing else in the query knows whether a cache is attached.

Six queries are cached: item, items, type, types, taxonomy, taxonomies. They share two helpers:

- `Helpers/CachedQueryExecutor.cs` runs the manager's `GetOrSetAsync`, translates a failed origin
  into `OriginUnavailableException` so a fail-safe manager may serve a stale copy, and reports where
  the value came from (`CachedQuerySource`).
- `Helpers/CachedItemsFetch.cs` is the item and listing fetch: fetch, process, and either store the
  hydrated value or a raw payload (`Caching/CachedRawItemsPayload.cs`) that is rehydrated on every
  hit, depending on the manager's `StorageMode`.

Languages, single elements and used-in never touch the cache. Feed enumeration
(`EnumerateItemsQuery.cs`, `Abstractions/SharedModels/DeliveryEnumeration.cs`) is a walk over
continuation tokens; a failed page throws `DeliveryRequestException`, because a walk has no single
result to carry a failure in.

The runtime-typed queries (`DynamicItemQuery.cs`, `DynamicItemsQuery.cs`) wrap the typed ones over
`IDynamicElements`, then ask the mapper to re-read each item as whatever type the type provider names
for its content type. They are not cached: the result type varies per item.

## Results

`IDeliveryResult<T>` (`Abstractions/SharedModels/`) is the one return shape: `Value`, `IsSuccess`,
`Error`, `StatusCode`, `HasStaleContent`, `ResponseHeaders`, `RequestUrl`, and two things the cache
adds - `ResponseSource` (`Origin`, `Cdn`, `Cache`, `FailSafe`) and `DependencyKeys`. Dependency keys
are on every successful result whether or not a cache is attached, so an application can tag its own
output cache with them.

## Deserialization and hydration

The converters (`Serialization/Converters/`) do two things at once: build the `ContentItem<T>` shell
with `System` and a deserialized `Elements`, and keep the item's raw JSON on it (`IRawContentItem`).
Everything after deserialization reads that JSON rather than the model:

- **Dependency keys** are read from the raw item, every `modular_content` entry and the elements of
  both (`ContentItems/Processing/ResponseDependencyExtractor.cs`), by the element's `type`
  discriminator. No model is consulted, so two models reading the same item carry the same keys and a
  raw-JSON cache entry can be shared between them. Asset elements yield no key; the caching guide's
  "Asset events" section says why and what to do instead.
- **Hydration** (`ContentItems/Mapping/ContentItemMapper.cs`) fills the complex elements - rich text,
  assets, taxonomy, linked items, date-time - onto the already-deserialized model, through compiled
  setters cached per model type (`PropertyMappingInfo.cs`). A value-type model is refused there, since
  a setter on a boxed copy would lose everything.
- **Linked items** (`LinkedItemResolver.cs`) are deserialized from `modular_content` on demand, to the
  type the provider names, and hydrated with the same mapper. The invariant that makes cycles work is
  *allocate, register, then populate*: an item goes into `MappingContext.ItemsBeingHydrated` before
  its elements are mapped, so a reference back to it - including back to the root - gets the same
  instance. Memoization is per root, not per response: a listing of twenty roots sharing one linked
  item hydrates it twenty times.

`ContentItems/TypeProvider.cs` is the default `ITypeProvider`: it looks for the source-generated
`GeneratedTypeProvider` in the entry assembly and the assemblies it references, and answers null for
everything when there is none, which keeps items dynamic. An application that registers its own
`ITypeProvider` replaces it. `ItemTypingStrategy.cs` turns the provider's answer into a decision per
item, with `IDynamicElements` meaning "keep the envelope".

## Rich text

`ContentItems/Processing/RichTextParser.cs` parses the element's HTML with AngleSharp once and turns
it into an SDK-owned block tree (`ContentItems/RichText/Blocks/`): HTML nodes with children, text,
inline images, content item links, and embedded content, which is a linked item or component resolved
through the same `LinkedItemResolver`. The public model exposes the tree, never AngleSharp.

Rendering is `ContentItems/RichText/Resolution/HtmlResolver.cs` over the options assembled by
`HtmlResolverBuilder.cs`. The resolver is immutable and shared; each render is a `RenderPass` that
owns the cancellation token and the child-resolver delegate, so concurrent renders do not interfere
and cancellation is checked before every block at every depth. Dispatch order per block: a type-based
resolver for embedded content, then a codename-based one, then a missing-resolver comment or
exception; for HTML nodes, the conditional resolvers in registration order, then the default.

## Caching

The contract is `Abstractions/Caching/IDeliveryCacheManager.cs`: `GetOrSetAsync` with a factory that
returns the value plus its dependency keys, `InvalidateAsync` over those keys, and a `StorageMode`
that tells the queries whether to store hydrated objects or raw JSON. `Caching/CacheKeyBuilder.cs`
makes the keys: readable, deterministic, order-independent, with filters hashed and the model type
appended only in hydrated-object mode.

`Kontent.Ai.Delivery.Caching` implements the contract over FusionCache (`FusionCacheManager.cs`) in
two shapes: memory, storing hydrated objects, and hybrid, storing raw payloads in a memory tier in
front of an `IDistributedCache`, with an optional backplane. Dependency keys are FusionCache tags.
The things easy to undo by accident there are commented at the point they matter: tag data lives ten
days so an invalidation outlives every entry it could apply to; reads fail open when the distributed
store is down; an invalidation does not, and reports `false` when it did not reach every tier; the
key prefix carries the environment id so two environments sharing one Redis cannot serve each other.

## Logging

`Logging/LoggerMessages.cs` holds every message as a source-generated `LoggerMessage`, with ids in
`LogEventIds.cs` grouped by area. Failures the SDK works around rather than surfaces - a cache entry
that would not deserialize, an error body it could not parse - are logged at Debug under
`Kontent.Ai.Delivery`, so a production investigation can turn them on without a code change.
FusionCache's own diagnostics go under its category when the application's logger factory is
registered.

## Tests

`Kontent.Ai.Delivery.Tests` is organised by the areas above. `Fixtures/` holds recorded API responses;
most tests run the real client over `MockHttpMessageHandler` so the handler chain and converters are
exercised, not stubbed. `Caching/` covers both managers with the in-memory distributed cache and a
switchable one for outages; the Redis tests run in CI against a service container and are skipped
elsewhere. The shared source under `src/common/` has its own tests under `src/testing/`, compiled into
each SDK's test project so the same assertions run against each assembly's copy.

Coverage is enforced per test project by a coverlet threshold in its `.csproj`; slopwatch runs over
the tree in CI with `.slopwatch/config.json` holding the suppressions, each with its reason.
