# Kontent.Ai.Delivery

Covers `Kontent.Ai.Delivery`, `.Abstractions`, `.Caching`, `.SourceGeneration` and `Kontent.Ai.Urls`,
which ship in lockstep on one version.

Entries before the move to this monorepo were imported from the GitHub Releases of
[kontent-ai/delivery-sdk-net](https://github.com/kontent-ai/delivery-sdk-net).

## Unreleased

### Breaking changes

- **Feed and used-in enumeration surfaces a failed page instead of silently truncating.** `EnumerateAsync()` ended its loop when a page request failed and yielded nothing to say so, so an interrupted walk was indistinguishable from a finished one — an export or a search-index build could quietly persist a partial result. It now throws `DeliveryRequestException`, carrying the status code, the API's `IError` and the request ID.

  This is the point of the change rather than a side effect: the defect could not be fixed without a behaviour break, because *not throwing* is precisely what was wrong. Code that relied on the documented graceful stop must now catch, or move to the single-request `ExecuteAsync`, which still reports failure as a value.

- **`EnumerateAsync()` returns `DeliveryEnumeration<T>` rather than `IAsyncEnumerable<T>`.** `DeliveryEnumeration<T>` *is* an `IAsyncEnumerable<T>`, so `await foreach` over items is unchanged and only a recompile is needed. It additionally offers `AsPages(continuationToken)`, a page view that exposes the continuation token so a walk can be checkpointed and resumed — which no route could do before.

- **`QueryEnumerationExtensions` is removed**, with all four `EnumerateItemsWithStatusAsync` overloads. It existed only because the item walk could not report failure, and that is now fixed at the source. The replacement is `EnumerateAsync().AsPages()` — but note this is not a rename: the element type changes from `IDeliveryResult<…>` to `DeliveryPage<T>`, and the `if (!result.IsSuccess)` branch becomes a `try`/`catch`.

  ```csharp
  // before
  await foreach (var page in query.EnumerateItemsWithStatusAsync())
  {
      if (!page.IsSuccess) { Handle(page.Error); break; }
      Process(page.Value.Items);
  }

  // after
  try
  {
      await foreach (var page in query.EnumerateAsync().AsPages())
      {
          Process(page.Items);
      }
  }
  catch (DeliveryRequestException ex) { Handle(ex); }
  ```

  The two used-in overloads also downcast to an internal interface and threw `NotSupportedException` for any query the SDK did not itself create; that wart goes with them.

- **A failed walk no longer emits the `PaginationStoppedEarly` warning** (event ID `1051`, now retired). The exception thrown in its place carries strictly more — status code, the API's `IError`, request ID and request URL — and the caller's `catch` is where a failure belongs in the log rather than a warning the SDK emits on its way to throwing. Anyone alerting on event ID `1051` should move to the exception, or to `QueryFailed` — see below.

- **`DeliveryClientFactory` is internal; resolve `IDeliveryClientFactory` instead.** The concrete factory was public with a constructor taking an `IServiceProvider`, so the only way to build one was to have a container already — at which point resolving the interface is what you would do anyway. Nothing in the SDK or its siblings constructed it, and the Management and Sync equivalents have always been internal. The interface is unchanged and still resolves from the container exactly as before; only `new DeliveryClientFactory(serviceProvider)` and references to the concrete type break.

- **`IItemTypingStrategy` and `IContentDeserializer` are gone from the public API.** Both sat in `Kontent.Ai.Delivery.Abstractions` as public interfaces with a single internal implementation each, and neither was a seam anyone could use. `IItemTypingStrategy` is now internal; `IContentDeserializer` was removed outright.

  `IContentDeserializer` could not be implemented at all. Its `DeserializeContentItem` returns `object`, but the value has to be a `ContentItem<TModel>` — an internal sealed record a consumer can neither construct nor derive from — and the raw-JSON cache path hard-cast to it. A custom deserializer therefore worked well enough on the uncached routes, which tolerate the result as `object`, and threw `InvalidCastException` the moment `CacheStorageMode.RawJson` rehydrated an entry. The migration notes for `19.0.0-rc1` suggested overriding its `JsonElement` overload; that advice should not have been given.

  With the interface gone, `ContentDeserializer` states what it actually returns. Callers that know the model type at compile time — the raw-JSON cache path — use `Deserialize<TModel>(JsonElement)` and get a `ContentItem<TModel>` back, so both casts disappear along with the reflection that fed them. Callers that only learn the type from a content type codename at runtime — linked items and dynamic runtime typing — use `Deserialize(JsonElement, Type)`, which returns `IContentItem` instead of `object`. The `string` overload had no callers left and went with it.

  `IItemTypingStrategy` was substitutable, since it only maps a codename to a `Type`, but it duplicates `ITypeProvider` one layer down and replacing it silently forfeited what the default adds: memoized lookups, the fallback to `DynamicElements` when a codename has no model, and the log line recording that fallback. `ITypeProvider` is the supported route for the same decision — it is public, documented, what the source generator emits against, and settable through `DeliveryClientBuilder.WithTypeProvider(...)`. Move any custom typing strategy there.

  With no interface left to be the default implementation *of*, `DefaultItemTypingStrategy` is now `ItemTypingStrategy`. The class is internal, so this is visible only as its logger category, which changes from `Kontent.Ai.Delivery.ContentItems.DefaultItemTypingStrategy` to `Kontent.Ai.Delivery.ContentItems.ItemTypingStrategy`. Event IDs are unchanged, so anything filtering on those is unaffected. The internal `IContentDependencyExtractor` went the same way; its implementation held no state and is now a static class.

### Added

- **`ExecuteAsync(continuationToken)` on the feed and used-in queries**, resuming a walk from a persisted cursor. Added as an overload rather than a parameter on the existing method, so `ExecuteAsync(cancellationToken)` keeps compiling.

- **`ContinuationToken` on `IDeliveryItemsFeedResponse` and `IDeliveryItemsFeedResponse<T>`**, making the feed's result-based route resumable across a process restart, which `FetchNextPageAsync` cannot be. `HasNextPage` is unchanged and remains equivalent to the token being present.

- **`ExecuteAsync()` on the used-in queries**, which previously had no single-request result-based route at all — only enumeration.

- **`DeliveryEnumeration<T>`, `DeliveryPage<T>` and `DeliveryRequestException`.** Both new types are sealed. `DeliveryEnumeration<T>` composes a page sequence through its constructor rather than being inherited from, so adapting another token-paged source is a delegate rather than a subclass — and `DeliveryEnumeration<T>.FromPages(...)` builds one over a fixed set of pages, for tests and fakes that no longer compile against the changed interfaces.

  `DeliveryPage<T>` is a plain class rather than a record: its only reference-typed member is a list, so synthesised equality would have compared that by reference and quietly reported two pages holding identical items as unequal. It stays immutable; it just does not claim value semantics it cannot deliver.

- **The feed and used-in queries now log like every other query.** They were the only family that never adopted the shared query-logging helper, so a failed `GetItemsFeed(...)` or `GetItemUsedIn(...)` produced no log line at all, where `GetItem`, `GetItems`, `GetTypes` and the rest have always emitted `QueryStarting`, `QueryFailed` (at `Error`) and `QueryCompleted`. They now do too.

  This reaches `FetchNextPageAsync()` as well: it issues one request, so it now emits the same bracket as `ExecuteAsync`. A manual page-by-page loop that previously logged only its first request will now log every one of them.

  A walk is the exception, and deliberately so: it is bracketed once by `PaginationStarted`/`PaginationCompleted` rather than emitting a starting/completed pair per page, since a 500-page walk should not produce 1,000 log entries. A failed page still reports `QueryFailed`, so no request fails silently on any route.


- **`DeliveryOptions.Timeout` bounds the whole call, and `IDeliveryOptionsBuilder.WithTimeout` sets it.** The ceiling on a request was decided entirely inside the SDK — lifted when its own resilience pipeline was installed, left at `HttpClient`'s 100-second default otherwise — with no way to read it off the options or change it. Supplying your own pipeline through `configureResilience` was the sharp case: a pipeline configured for two minutes was still cut off at 100 seconds, silently.

  `Timeout` is unset by default and nothing changes for anyone who leaves it alone. Set it and it always wins, whatever the pipeline; `Timeout.InfiniteTimeSpan` removes the ceiling outright. It outranks `Retry-After`: the API's backoff is honoured in full until the budget runs out, then the call is cut short.

  ```csharp
  services.AddDeliveryClient(o => { o.EnvironmentId = "…"; o.Timeout = TimeSpan.FromMinutes(5); });
  ```
### Unchanged, deliberately

- `FetchNextPageAsync()` stays on the feed response. It performs one request and returns a result, so it belongs to the same half of the contract as `ExecuteAsync` and is not a duplicate of the walk: *step* (`FetchNextPageAsync`), *resume* (`ExecuteAsync(token)`) and *walk* (`AsPages()`) answer three different questions.
- The offset-paged listings (`GetItems`, types, taxonomies, languages) are untouched. They carry `Skip`, `Limit` and `TotalCount` that a forward-only page cannot express, they support random access, and nothing about them is broken.

### Fixed

- **The `X-KC-SOURCE` header names the calling assembly when a tool declares a version but no package name.** `[assembly: DeliverySourceTrackingHeaderAttribute(null!, 1, 2, 3)]` composed the header as `";1.2.3"` — a leading separator identifying nothing. It now falls back to the assembly's own name, as it already did when the version was read from the assembly.

- **A missing named client says which call registers one.** `IDeliveryClientFactory.Get(name)` let the container raise the failure, so an unregistered name produced its generic "No service for type … has been registered" rather than naming the client or the fix. It now reports `No delivery client registered with name '…'. Ensure you've registered the client using AddDeliveryClient("…", ...)`, matching the Management and Sync SDKs. Still an `InvalidOperationException`; only the message changes.

- **A transport failure reports what actually went wrong.** A DNS failure, a refused connection or a resilience-pipeline rejection produced `Error.Message` of `"Unknown error"`, discarding the exception's own message while keeping it reachable only through `Error.Exception`. The message now carries it — `"No such host is known."` rather than `"Unknown error"`. The exception is still on `Error.Exception` as before.

## 20.0.0-rc.2 (2026-08-12)  _(prerelease)_

### Breaking changes

- **The caching package's registration class is renamed to `DeliveryCacheServiceCollectionExtensions`.** It and the Delivery SDK both declared `Kontent.Ai.Delivery.ServiceCollectionExtensions`, so two packages owned one full type name — and since `Kontent.Ai.Delivery.Caching` depends on `Kontent.Ai.Delivery`, every consumer has both and could name neither: referring to it was `CS0433`, with no way to disambiguate. Nothing that compiled before stops compiling. The namespace is unchanged, so `using Kontent.Ai.Delivery;` and every `services.AddDeliveryMemoryCache(...)` / `AddDeliveryHybridCache(...)` / `AddDeliveryCacheManager(...)` call is exactly as it was; only code that named the type explicitly is affected, and that could not have built.

- **The source generator emits its marker attribute as `internal`.** `ContentTypeCodenameAttribute` is generated into each referencing compilation, so a `public` one put the same type name into every assembly that uses the generator. Two such projects referencing each other stopped compiling with `CS0436`/`CS0433`, and the only fix available to the consumer was to drop a project reference. Emitting it `internal` — standard practice for generated marker attributes — gives each assembly its own copy. Code that only applies the attribute to its own models is unaffected; code that exposed it across an assembly boundary was in the broken configuration already.

### Added

- **`ConfigureFusionCache`** on `DeliveryCacheOptions`, from `Kontent.Ai.Delivery.Caching`, configures the underlying cache with `FusionCacheOptions` typed:

  ```csharp
  services.AddDeliveryMemoryCache(opts => opts
      .ConfigureFusionCache(fusion => fusion.DefaultEntryOptions.EagerRefreshThreshold = 0.8f));
  ```

  The `ConfigureFusionCacheOptions` property it sets stays as it was, `Action<object>?`, because it is declared in `Kontent.Ai.Delivery.Abstractions` and that package deliberately references nothing. The extension lives where FusionCache is already referenced, so the cast happens once here instead of in every caller.

### Fixed

- **`DeliveryClientBuilder.Build()` documents the exception it actually throws.** It promised `InvalidOperationException` for invalid configuration; the validation runs in the options pipeline, so what surfaces is `OptionsValidationException`. Now pinned by a test, and the inline note about *why* it fires during the build is corrected too.

- **The caching package no longer re-registers the dependency extractor the SDK already registers**, and the interface no longer describes a no-op implementation that does not exist — there is one implementation.

- **`IDeliveryClient` says which queries are cached.** Languages, single content elements and used-in queries always reach the API; that was a decision nowhere written down.

- **`DeliverySourceTrackingHeaderAttribute` is sealed**, and both `WithEnvironmentId` overloads describe setting the environment rather than constructing the builder.

- **A rich-text document is disposed once parsed.** The AngleSharp document was left to finalization on every rich-text element mapped; the parsed blocks hold plain strings and lists rather than document nodes, so nothing needed it to stay alive.

- **A client name containing a tab or newline is rejected like one containing a space.** The rule trimmed and then looked for spaces, so other whitespace passed validation and left a name that is invisible at the point of failure. The caching package also carried its own copy of the rule, which is now the shared one.

- **Dynamic queries carry their dependency keys, on every page.** `GetItem`/`GetItems` without a typed model returned results whose `DependencyKeys` were `null`, while the typed queries forwarded them — so output-cache tagging, which those keys exist for, had nothing to tag with on the dynamic path. Paging through a dynamic listing dropped them the same way from the second page on.

- **`ImageUrlBuilder` keeps a query the asset URL already carries.** Transformations were applied as a relative reference with its own query, which replaces the base URL's query outright. An asset URL produced by a default rendition preset therefore lost its rendition the moment any transformation was added. The two are merged now, with an explicit transformation winning where both set the same key.

- **A cache miss in raw-JSON mode hydrates once instead of twice.** The factory already builds the value to collect its dependency keys, and the payload it stored was then parsed and mapped a second time to answer the same call. The call that produced the value now reuses it; a cache hit or a background refresh still rehydrates, as it must.

- **The source generator no longer pins compilations in the IDE's incremental cache.** Its pipeline model carried a `Location`, which holds its `SourceTree` alive — and pipeline values are retained for as long as the generator is loaded, so every edit accumulated another rooted syntax tree and the compilation behind it. The position is stored as a path and spans, and the `Location` is rebuilt only when a diagnostic is reported.

- **Options handed to the SDK prebuilt are copied by reflection rather than property by property.** `DeliveryOptions.CopyTo` listed the properties it carried, which keeps compiling when an option is added and silently stops carrying it — a value the caller set that the client never sees. It now uses the same copier the other SDKs do.

- **The `X-KC-SOURCE` header keeps naming the integration that made the call.** Attribution matched the SDK assembly by full name, which carries the version — and nothing pins `AssemblyVersion`, so the reference an integration recorded when it was built stopped matching on the first SDK release after that. The header then went silently missing for every consumer who had not rebuilt. Matching is now by simple name.

- **Rich-text tag resolvers keep their place in registration order, and the description is no longer dispatch.** `WithHtmlNodeResolver(tagName, ...)` registrations were lifted into a lookup consulted before any predicate resolver, so a tag resolver won however late it was registered — against the documented "evaluated in registration order, first match wins". Membership of that lookup was decided by whether the resolver's *description* started with `Tag=`, so a predicate resolver a caller happened to describe that way was silently promoted into it. Registering the same tag twice threw an `ArgumentException` from `Build()`, where every other registration is resolved by order. All three now follow the one documented rule: one ordered pass, first match wins, tag registrations included. The public builder API is unchanged.

- **Cache keys are scoped to the environment they were fetched from.** A key was built from the query alone, so "the item `article`" produced the same key in every environment. Two applications sharing one distributed cache and pointing at different environments served each other's content, silently and in both directions. The environment id is now part of the key prefix, ahead of which an explicit `KeyPrefix` still separates clients within one environment. Existing distributed cache entries are not readable under the new keys and are simply missed once, then rewritten — nothing to migrate, but expect one cold start after upgrading.

- **An application's own `JsonSerializerOptions` registration is no longer taken over as the SDK's wire serializer.** `AddDeliveryClient` looked for a singleton registered under `JsonSerializerOptions` and, finding one, used it to read every API response. Registering that type is an ordinary thing for an application to do, and the options it registers do not carry `ContentItemConverterFactory` - without which no raw item JSON is captured, hydration has nothing to map from, and typed models come back empty. Nothing threw and nothing was logged. The SDK now keeps its serializer under a type only it names, so an application's registration stays the application's, and a registration made through a factory or under a service key no longer splits Refit and the mappers onto different serializers.

- **A distributed cache no longer strips taxonomy and multiple-choice data out of content types.** The distributed tier's serializer was built without the SDK's own converters, so it wrote content type elements by their declared type: `TaxonomyElement.TaxonomyGroup` and `MultipleChoiceElement.Options` went in and never came out. A node reading such an entry back got a plain `ContentElement` - an `InvalidCastException` for anything casting to `ITaxonomyElement` or `IMultipleChoiceElement`, and missing data for anything that did not. The writing node was unaffected because it answers from its own memory tier, so this surfaced only on a second instance, which is the case a distributed cache exists for. `ContentElementConverter` now writes an element by its runtime type - the wire's own `type` field is the discriminator on the way back - and the distributed tier uses the SDK's serializer unless one is supplied.

- **A request is bounded again when the SDK's own resilience pipeline is not the one installed.** `HttpClient.Timeout` was set to `Timeout.InfiniteTimeSpan` unconditionally, on the premise that the resilience pipeline owns timing - but the 30-second per-attempt timeout that premise rests on exists only while `EnableResilience` is left on and no `configureResilience` hook replaces the default pipeline. Setting `EnableResilience = false`, or supplying a pipeline that adds no timeout of its own, therefore left a call with no attempt timeout, no overall timeout and no ceiling of any kind, so a connection that stopped responding hung the caller indefinitely. The ceiling is now lifted only for the default pipeline; otherwise `HttpClient`'s 100-second default applies, as it did before this SDK moved to a resilience pipeline. A custom pipeline that legitimately needs longer can raise it through `configureHttpClient`.

- **An attempt the resilience pipeline timed out is now retried instead of failing the whole call.** The default pipeline wraps retry around a 30-second per-attempt timeout, so a hung attempt reaches the retry as Polly's `TimeoutRejectedException` - a type the SDK's transient classifier did not recognise. The single situation that per-attempt timeout exists for, a connection that stops responding and that a fresh attempt would recover from, therefore failed the whole call after 30 seconds with no retries at all.

## 20.0.0-rc.1 (2026-08-07)  _(prerelease)_

Targets .NET 10. Every package in this product moves from `net8.0` to `net10.0`, which is why this is a major release, and Refit's transport is upgraded across four major versions. Beyond the target framework the public API is almost untouched — one configuration hook is removed, and two request-building details change in ways that are visible in logs but not in results.

### Breaking changes

- **`net8.0` → `net10.0`.** There is no multi-targeting, so a project on .NET 8 cannot install this release at all — restore fails with `NU1202: Package Kontent.Ai.Delivery is not compatible with net8.0`. Move to .NET 10 first. `Kontent.Ai.Delivery.SourceGeneration` is the exception and stays `netstandard2.0`, as Roslyn components must; it loads in every SDK from .NET 8.0.2xx onward, unchanged.
- **The client interfaces no longer carry `IDisposable` / `IAsyncDisposable`; the concrete clients do.** Disposal exists for one situation - a client built outside a container, which owns its own transport and must release it. Putting it on the interface meant every consumer holding `IDeliveryClient` was offered a `Dispose()` that, on the container path, released nothing and must not be called: the container owns that lifetime. `DeliveryClientBuilder.Build()` now returns the concrete `DeliveryClient`, which is `IDisposable` and `IAsyncDisposable`, so disposal stays available exactly where it means something.

  `await using var client = DeliveryClientBuilder…Build();` is unchanged, and so is every DI usage. The only code that breaks widened the builder result to the interface and then disposed it:

  ```csharp
  // Before - no longer compiles, because the interface has no Dispose
  IDeliveryClient client = DeliveryClientBuilder…Build();
  client.Dispose();

  // After - keep the concrete type, or just use var
  var client = DeliveryClientBuilder…Build();
  client.Dispose();
  ```

  Container-resolved clients are still disposed by the container, which checks the runtime type rather than the registered service type - so nothing changes there.
- **The `configureRefit` parameter is gone from all six `AddDeliveryClient` overloads**, and `RefitSettingsProvider` is now internal. The hook exposed the transport library's settings object, but everything reachable through it was load-bearing rather than configurable: the parameter-key formatter is what matches the API's casing, and the serializer options carry the converters and nesting limit the wire format requires. Overriding any of them broke requests silently. If you passed `configureRefit`, delete the argument — the SDK's own tests never used it for anything but asserting the callback fired. Should a real need surface, it will return as an API named for what it does rather than for the transport library.

### Changed

- **Registering from `IConfiguration` can now customize the HTTP client and the resilience pipeline.** The configuration-based `AddDeliveryClient` overloads took no `configureHttpClient` / `configureResilience`, so binding options from configuration and replacing the retry pipeline were mutually exclusive. The workaround — binding by hand inside an `Action<DeliveryOptions>` — compiles and looks equivalent, but registers no change token, so `IOptionsMonitor` silently stops reloading. Both hooks are now available on every configuration overload, alongside the ones that already had them.
- **`AddDeliveryClient` gained the overloads its sibling SDKs already had**, so the three register a client the same way: named `IConfiguration` and `IConfigurationSection` registration. Nothing was removed, and existing calls are unaffected — this closes gaps rather than reshaping the surface.
- **`CacheResult<T>` carries `FromFactory`**, saying whether the factory produced the value during this call or the cache served a stored one. Nothing else about the type changes and the constructor is untouched, so existing code compiles unchanged. A custom `IDeliveryCacheManager` that builds its own `CacheResult<T>` should set it — left at its `false` default, every result it returns is treated as a cache hit.
- **`DeliveryOptions.DefaultConfigurationSectionName`** exposes the section name the configuration overloads bind by default, matching `ManagementOptions`. Design-time tools that resolve the SDK's configuration from the same sources can probe it instead of hard-coding the string.
- **Repeated filter parameters are emitted in declaration order.** Filters sharing an element used to be grouped together regardless of where they appeared, so `a`, `b`, `a` was sent as `a`, `a`, `b`. Results are unaffected — filter parameters are AND-ed, and cache keys were already order-independent — but the request URL differs, which shows up in logs and traces.
- **Cancellation now throws; other transport failures are results.** Refit's upgrade changed the
  contract: exceptions raised in the HTTP pipeline are captured into the response rather than thrown.
  A network failure, DNS failure or resilience-pipeline rejection is therefore an unsuccessful result
  carrying the exception, consistent with how every other failure in this SDK is reported. Cancellation
  is the exception to that: when the caller's token fires, the `OperationCanceledException` is rethrown,
  so `Task.IsCanceled`, `Task.WhenAll` and cancellation handlers behave as they do everywhere else in
  .NET. Previously **all** of these threw. An expired `HttpClient.Timeout` is *not* cancellation, even
  though .NET surfaces it as a `TaskCanceledException`: the request was sent, so it is reported as a
  failed result like any other transport failure.
- **Transport failures report status `0`.** the result object now carries `(HttpStatusCode)0` for that case rather than an invented code. Responses that did arrive are unaffected.

### Fixed

- **An element the model cannot map is now logged as a warning rather than at debug level.** When a value fails to deserialize onto the generated property, the SDK logs and yields `null` — but `null` is also what an empty element gives, so the log is the only thing distinguishing the two. At `Debug` it was absent from any normal production configuration, which made a model that had drifted from the content type look like missing content instead of a mismatch. The behaviour is unchanged; only the level is.
- **Cache invalidation propagates reliably between nodes once a backplane is registered.** With `AddDeliveryHybridCache`, part of the invalidation state is held per `IDeliveryCacheManager` instance, so whether one node observes another's invalidation depends on the order the two nodes happened to read and invalidate in. In one measured ordering — node A caches an entry, node B reads it, then A invalidates — B keeps serving the evicted content until the entry expires on its own. Register an `IFusionCacheBackplane` and the SDK now wires it up, so invalidations propagate regardless of ordering:

  ```csharp
  services.AddStackExchangeRedisCache(o => o.Configuration = "localhost");
  services.AddFusionCacheStackExchangeRedisBackplane(o => o.Configuration = "localhost");
  services.AddDeliveryHybridCache();
  ```

  Nothing changes for a single-instance application, which needs no backplane. FusionCache keeps an in-memory tier in front of the distributed one and uses it either way; the backplane is what keeps those tiers in step across nodes, so without one an invalidation still reaches only the node that performed it.
- **Cache invalidation no longer skips items whose codename looks like a component's.** Components were told apart by the shape of their generated codename — a `_`-separated group of four characters starting `01`, as in `n373888cc_34e2_01e1_1820_3cb52ab1b2a1`. Authored codenames collide with that: `Product SKU 0123 Blue` becomes `product_sku_0123_blue`, whose third group is `0123`. Such an item was silently given no `item_` dependency key, so a webhook naming it evicted nothing and the cached response kept being served until it expired — the failure was invisible and depended on how content was named.

  Components are now recognised from the response instead: the Delivery API gives every content item a `workflow` and `workflow_step` and gives components neither. Where that signal is not available the item is tracked regardless, because the two mistakes are not equal — a dependency key for a component is one entry nobody ever looks up, while a missing key for an item is stale content.
- **Eager refresh no longer misreports what a query returned.** With `EagerRefreshThreshold` set, the cache returns the stale-but-valid value immediately and refreshes it on a background thread. Each query builder decided whether it was serving a cache hit by reading variables its own cache factory wrote into — and the background refresh writes into those same variables, for a different call. Depending on how the two threads interleaved, an ordinary eager-refresh hit could come back as `ResponseSource.FailSafe`, or be logged as a fresh fetch and wrapped with the background request's status code. The decision now comes from the cache, which is the only component that knows which value it handed back. Consumers who leave `EagerRefreshThreshold` at its default of `0` were never affected.
- **Rich-text parsing and resolution no longer risk deadlocking a caller that blocks on the task.** Every `await` in the SDK opts out of the caller's synchronization context — except the rich-text subsystem, which had drifted: 17 awaits across the parser, the HTML resolver and the default resolvers captured the context. On a host that has one (WPF, WinForms, legacy ASP.NET), code that blocks on the returned task — `.Result`, `.Wait()` — could deadlock. ASP.NET Core has no synchronization context and was never affected. CA2007 is now enforced across the SDK libraries so this cannot drift again.
- **The configured retry pipeline can now run to completion.** `HttpClient`'s 100-second default bounds the *whole* call, retries and backoff included, and nothing raised it — so a pipeline allowed four 30-second attempts plus exponential backoff was silently cut off partway through the last one. The SDK's resilience pipeline already bounds each attempt, so it now owns timing outright and the transport-level ceiling is removed. Requests still stop when your `CancellationToken` fires.
- **Long-running applications pick up DNS changes instead of pinning the address resolved at startup.** The registered client is a singleton and takes its `HttpClient` from `IHttpClientFactory` once, so the handler chain it holds was never rotated — the factory only hands a fresh chain to a *new* `CreateClient` call. Connections now recycle every two minutes, matching the factory's own default handler lifetime. This matters when the endpoint's address changes underneath a process that stays up for days: a failover, a scale event, or any CDN re-pointing. Configuring your own primary handler via `configureHttpClient` still overrides this, as before.
- **Retried requests no longer accumulate duplicate `X-KC-SDKID` headers.** The tracking handler sits below the resilience handler, so every retry re-runs it against the same request message. The header was appended rather than replaced, adding one duplicate value per attempt. Writes are now idempotent. Only the outgoing request differed; results were unaffected.
- **A failure resolving the `X-KC-SOURCE` header no longer breaks every later request.** The value is cached in a `Lazy<string?>`, and the resolution walks the call stack to attribute the calling package. An exception thrown during that walk was cached alongside the value and rethrown on every subsequent request for the lifetime of the process. Resolution failures are now contained and the header simply omitted, which was the intent.
- **Filter codenames are normalized to lower case, so one query no longer occupies two cache entries.** The Delivery API is case-insensitive here, so `System.Codename[EQ]` and `system.codename[eq]` always returned the same items — but the SDK hashed the filter key verbatim when building cache keys, so the two spellings cached separately, doubled origin calls, and invalidated independently. Codenames are lower case by construction, so this can only change input that was already unconventional.
- **An empty pre-release label no longer produces a trailing hyphen in `X-KC-SOURCE`.** A package identifying itself with `[assembly: DeliverySourceTrackingHeader("MyPackage", 2, 0, 0, "")]` was reported as `MyPackage;2.0.0-`, which is not a valid SemVer version. An empty label now counts as no label, matching what passing `null` already did.
- **HTTP responses are released as soon as they are mapped, instead of waiting for finalization.** Every query turned its Refit response into a result without disposing it, so each `HttpResponseMessage` and its buffered content stayed alive until the garbage collector ran the finalizer. Refit reads the body in full, so connections still returned to the pool and no request ever failed — the cost was memory pressure that grew with throughput, and it was heaviest where responses come in volume, such as enumerating a whole project a page at a time. The Management and Sync SDKs already disposed here; Delivery now matches them.

### Dependencies

Shipped floors on `Kontent.Ai.Delivery`, `Kontent.Ai.Delivery.Caching` and `Kontent.Ai.Urls` moved up. All are .NET 10 aligned:

- `Microsoft.Extensions.*` (`Configuration`, `.Binder`, `Options`, `.ConfigurationExtensions`, `.DataAnnotations`, `Primitives`, `Logging.Abstractions`) **9.0.15** → **10.0.10**.
- `Microsoft.Extensions.Http.Resilience` **9.6.0** → **10.8.0**.
- `Microsoft.Extensions.Caching.Abstractions` **8.0.0** and `.Caching.Memory` **10.0.6** → **10.0.10**; `.Caching.StackExchangeRedis` **8.0.26** → **10.0.10** (`Kontent.Ai.Delivery.Caching`).
- `AngleSharp` **1.5.0** → **1.7.0**.
- `ZiggyCreatures.FusionCache` and its `System.Text.Json` serializer **2.5.0** → **2.6.0** (`Kontent.Ai.Delivery.Caching`).
- `Refit` and `Refit.HttpClientFactory` **10.2.0** → **14.0.1**.

`Kontent.Ai.Delivery.Abstractions` ships no package dependencies of its own and is unaffected.

### Internal

No consumer-visible effect:

- Refit 14 builds request logic at compile time instead of by reflection. The filter DSL, whose parameter names are only known at runtime, now renders its own query string and applies it through a message handler, so every operation compiles to generated code and no reflection package is needed. The escaping this produces is byte-for-byte what the previous transport emitted, pinned by a characterization test suite covering reserved characters, pre-encoded input, non-ASCII values, empty operators and repeated keys.
- **A client built by `DeliveryClientBuilder` now owns its service provider directly**, rather than being handed back inside a wrapper whose only jobs were to forward every `IDeliveryClient` member and dispose the provider behind it. Both entry points construct through one factory, so the container path and the builder path cannot drift; the builder passes the provider as the resource its client owns. Disposal behaves exactly as before - disposing a built client tears down its provider and everything registered in it, including the cache manager and anything added via `ConfigureServices`, and disposing a container-resolved client still releases nothing, because the container owns its transport.
- `Kontent.Ai.Delivery.SourceGeneration` deliberately compiles against an older Roslyn than the rest of the repo. That reference sets the oldest compiler able to *load* the generator, and a newer one would be skipped silently on older SDKs.


## 19.4.0 (2026-08-03)

Maintenance release: restores buildability against NuGet signature verification and refreshes the dependency estate. **No public API, behavior, or target framework changes** — no production source file changed since 19.3.1.

#### Fixed

- `Refit` and `Refit.HttpClientFactory` **10.1.6** → **10.2.0**, resolving restore failures ([#417](https://github.com/kontent-ai/delivery-sdk-net/issues/417)). The certificate that author-signed 10.1.6 was revoked, so NuGet signature verification failed with `NU3012` during `dotnet restore` on any pipeline with verification enabled ([reactiveui/refit#2114](https://github.com/reactiveui/refit/issues/2114)). 10.2.0 is the same code re-signed with a valid certificate — no API or behavior difference.
- CI no longer disables NuGet signature verification. The `DOTNET_NUGET_SIGNATURE_VERIFICATION` bypass added as a stopgap has been removed now that a validly signed Refit is available.

#### Dependencies

Shipped dependency floors on `Kontent.Ai.Delivery` and `Kontent.Ai.Urls` moved up:

- `Microsoft.Extensions.Options.DataAnnotations` **8.0.0** → **9.0.15**. This is a major-version floor raise and the reason this is a minor rather than a patch release — if you pin the `Microsoft.Extensions.*` graph at 8.x, restore may report `NU1605` until you align on 9.x. There is no compile or runtime impact: the 9.x packages still target `net8.0`.
- `Microsoft.Extensions.Configuration`, `.Configuration.Binder`, `.Options`, `.Options.ConfigurationExtensions`, `.Primitives`, `.Logging.Abstractions` **9.0.3** → **9.0.15** (servicing band).

`Kontent.Ai.Delivery.Abstractions` and `Kontent.Ai.Delivery.Caching` are dependency-identical to 19.3.1.

#### Internal

Build and test-only changes. None of these packages ship in the SDK:

- The build engine is pinned to the .NET 10 SDK (`global.json`, `10.0.300` with `latestPatch` roll-forward), aligning with the rest of the .NET monorepo estate ([#416](https://github.com/kontent-ai/delivery-sdk-net/pull/416)). **This has no effect on consumers** — all packages still target `net8.0` (`netstandard2.0` for the source generator). It does affect contributors: building the repo now requires the .NET 10 SDK.
- Test assertions migrated from `FluentAssertions` `[7.2.2,8.0.0)` to `AwesomeAssertions` **9.4.0**, the community Apache-2.0 successor. v9 uses its own `AwesomeAssertions` namespace, so test files import that instead.
- Workflow checkouts hardened and action versions modernized ([#415](https://github.com/kontent-ai/delivery-sdk-net/pull/415)).
- Tooling refresh: `Microsoft.SourceLink.GitHub` 8.0.0 → 10.0.202, `Microsoft.NET.Test.Sdk` 17.14.1 → 18.4.0, `xunit.runner.visualstudio` 2.8.2 → 3.0.2, `RichardSzalay.MockHttp` 6.0.0 → 7.0.0, `coverlet.collector`/`coverlet.msbuild` 3.2.0 → 6.0.4, `SonarAnalyzer.CSharp` 10.19.0.132793 → 10.23.0.137933.

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.3.1...19.4.0

## 19.3.1 (2026-07-30)

### Kontent.ai .NET Delivery SDK 19.3.1

Patch release: picks up a security fix in AngleSharp. No public API or behavior changes.

**Security**

- Upgraded AngleSharp from 1.3.0 to 1.5.0, resolving CVE-2026-54570 — mXSS via a MathML annotation-xml HTML integration point bypass.
- Delivery is only partially exposed to this. Rich text parsing goes through AngleSharp, but serialization does not: the SDK rebuilds parsed content into its own block tree and renders it through HtmlResolver, which HTML-encodes attribute values and text nodes instead of using AngleSharp's formatter. The upgrade clears the advisory for every consumer regardless.

**Internal**

Test-only dependency updates. Neither package ships in Kontent.Ai.Delivery:

- Microsoft.CodeAnalysis.CSharp.Analyzer.Testing 1.1.2 → 1.1.4, which drops a transitive System.Formats.Asn1 5.0.0 (GHSA-447r-wph3-92pm).
- FluentAssertions moved to the range [7.2.2,8.0.0) — takes the newest Apache 2.0 release and prevents the build from drifting into the commercially licensed 8.x.

Full Changelog: https://github.com/kontent-ai/delivery-sdk-net/compare/19.3.0...19.3.1

## 19.3.0 (2026-06-24)

Maintenance release: HTTP transport alignment with the modernized Management SDK, plus internal streamlining of dependency-injection registration and options handling. The public API is unchanged.

#### Dependencies

- Upgraded `Refit` and `Refit.HttpClientFactory` from **8.0.0** to **10.1.6**, and moved the `Microsoft.Extensions.*` dependencies to the **9.x** servicing band.
- This brings Delivery onto the same Refit and `Microsoft.Extensions.Http` versions as the modernized `Kontent.Ai.Management` SDK, so both can be referenced in a single project (e.g. the Kontent.ai model generator) with one Refit in the dependency closure.
- Target framework is unchanged (`net8.0`). No code changes were required — Delivery already uses `System.Text.Json` for Refit serialization.

#### Internal improvements

These are implementation-only changes with no public API or behavior impact:

- DI registration and runtime options access are streamlined behind a single internal options accessor. The `DeliveryClient` and the authentication handler no longer thread an `IOptionsMonitor` plus client name around; they read effective options through one abstraction, which also removes a duplicate handler constructor.
- Options copying during client registration no longer uses reflection — values are copied explicitly, dropping the internal reflection-based helper.

#### Upgrade notes

- `RefitSettings` (exposed via `RefitSettingsProvider.CreateDefaultSettings()` and the `configureRefit` parameter on `AddDeliveryClient(...)`) now resolves from Refit 10. Its public shape is unchanged, so existing configuration code compiles as-is.
- If your application **directly** references `Refit` 8.x, it will now float to 10.x and inherit Refit's own 8→10 breaking changes. Review the [Refit release notes](https://github.com/reactiveui/refit/releases) if you call Refit APIs directly.

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.2.0...19.3.0

## 19.2.0 (2026-05-04)

Minor release adding a canonical empty value for rich text and sealing `RichTextContent` against post-construction mutation. Pre-requisite for upcoming improvements to model generator (default values instead of strict nullability everywhere).

> [!IMPORTANT]
> **Upgrading from 18.x?** v19 is a ground-up redesign of the SDK. Read the [upgrade guide](docs/upgrade-guide.md) before you start.

#### What's new

- `RichTextContent.Empty` — shared singleton representing the `<p><br></p>` value Kontent.ai returns for empty rich text.
- `RichTextContent` is now immutable. Blocks and metadata are supplied at construction time only; the internal `AddRange` method and the `Links` / `Images` / `ModularContentCodenames` setters are gone.

```csharp
public record Article
{
    public RichTextContent BodyCopy { get; init; } = RichTextContent.Empty;
}
```

#### Migration from 19.1

The public parameterless `RichTextContent()` constructor has been removed. It produced a `Count == 0` instance that could not be populated through any public API — effectively unusable. Replace any call sites with `RichTextContent.Empty`, or remove them.

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.1.0...19.2.0

## 19.1.0 (2026-04-27)

Minor release adding `IServiceProvider`-aware DI registration overloads so SDK options can be composed from sibling services already registered in the container.

> [!IMPORTANT]
> **Upgrading from 18.x?** v19 is a ground-up redesign of the SDK. Read the [upgrade guide](docs/upgrade-guide.md) before you start.

#### What's new

- `(IServiceProvider, options)` overloads on `AddDeliveryClient`, `AddDeliveryMemoryCache`, `AddDeliveryHybridCache`, and the matching `DeliveryClientBuilder.WithMemoryCache` / `WithHybridCache` builder methods.
- `configureRefit` forwarding on the default advanced `AddDeliveryClient(...)` overloads, for parity with named-client registration.
- Repeated cache registration for the same client is now explicitly last-wins — earlier keyed `IDeliveryCacheManager` registrations are removed before the new one is added.

```csharp
services.Configure<SiteOptions>(configuration.GetSection("Site"));

services.AddDeliveryMemoryCache("production", (sp, options) =>
{
    var site = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
    options.DefaultExpiration = site.CacheExpiration;
    options.IsFailSafeEnabled = true;
});
```

The cache callback runs the first time the cache manager is resolved, not at registration time. Resolve only singleton-safe dependencies (`IOptions<T>`, `IOptionsMonitor<T>`, configuration, loggers) — not `IOptionsSnapshot<T>` or other scoped services.

#### Migration from 19.0

No code changes required. Use the new `(IServiceProvider, options)` overloads when SDK options need to be composed from other DI services; keep using the plain `Action<...>` overloads when you want eager validation at registration time.

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.0.0...19.1.0

## 19.0.0 (2026-04-19)

Production release of the revamped 19.0 SDK, consolidating pre-release iterations into a stable GA. Changes vs RC5: SDK tracking headers no longer leak SourceLink build metadata, and per-content-type cache invalidation tags now flow through to cached item entries.

> [!IMPORTANT]
> **Upgrading from 18.x?** 19.0 is a ground-up design overhaul of the SDK — every public surface area has changed. Read the [upgrade guide](docs/upgrade-guide.md) and the [quick migration checklist](docs/upgrade-guide.md#quick-migration-checklist) before you start.

#### Public API surface changes

19.0 replaces the 18.x public API across every area:

- **Query methods** — `GetItemAsync<T>(...)` / `GetItemsAsync<T>(...)` replaced by the fluent `GetItem<T>("codename").ExecuteAsync()` / `GetItems<T>().ExecuteAsync()` builder pattern.
- **Filtering** — Parameter classes (`EqualsFilter`, `InFilter`, `LanguageParameter`, …) replaced by fluent `.Where(...)`, `.Language(...)`, `.OrderBy(...)`, etc.
- **Response handling** — Direct response types replaced by `IDeliveryResult<T>` / `IDeliveryItemListingResult<T>` with a result pattern (`IsSuccess` / `Value` / `Error` / `StatusCode` / `DependencyKeys` / `ResponseSource`).
- **Model structure** — Flat properties (`item.Title`) replaced by `IContentItem<T>` wrapper (`result.Value.System`, `result.Value.Elements.Title`).
- **Caching** — Legacy `Kontent.Ai.Delivery.Caching` replaced by the FusionCache-backed redesign (`MemoryCacheManager` / `HybridCacheManager`, tag-based invalidation, `DependencyKeys`, `CacheResult<T>`, `CacheStorageMode`).
- **Resilience** — `IRetryPolicy` replaced by Polly-based `configureResilience` pipelines.
- **Rich text** — `IContentLinkUrlResolver` / `IInlineContentItemsResolver<T>` replaced by the `HtmlResolverBuilder` fluent API.
- **DI registration** — New overloads, keyed services, named clients, and `ConfigureServices(...)` hook; `AddDeliveryClientCache` split into `AddDeliveryMemoryCache` / `AddDeliveryHybridCache`.
- **Sync API** — Removed from this package; moved to the standalone [`Kontent.Ai.Sync`](https://github.com/kontent-ai/sync-sdk-net) package.

The full type-by-type surface diff, including renamed / removed / added members across every RC, is enumerated in the [upgrade guide](docs/upgrade-guide.md). The public-API approval snapshot (`Kontent.Ai.Delivery.Abstractions.Tests/ApiApproval/PublicApiApprovalTests.PublicApi_ShouldNotChangeUnexpectedly.verified.txt`) is the authoritative reference for the 19.0.0 surface.

#### Bug fixes from rc5

- **SDK tracking headers no longer leak build metadata** — The `X-KC-SDKID` and `X-KC-SOURCE` headers previously included the SourceLink-appended commit SHA (e.g. `nuget.org;Kontent.Ai.Delivery;5.0.0-rc.5+a1b2c3d`) because the SDK derived the version from `FileVersionInfo.ProductVersion`, which mirrors `AssemblyInformationalVersionAttribute` and therefore carried the `+metadata` suffix when built with SourceLink + `ContinuousIntegrationBuild`. Both headers now emit clean SemVer (`nuget.org;Kontent.Ai.Delivery;5.0.0-rc.5`). Pre-release labels are preserved; only the SemVer build-metadata suffix (everything from `+` onward) is stripped. Version extraction no longer performs disk I/O via `FileVersionInfo.GetVersionInfo(assembly.Location)`, improving reliability under NativeAOT, trimming, and single-file publishing. The legacy Xamarin Android `FileNotFoundException` workaround has been removed.

#### Improvements from rc5

- **Per-content-type cache invalidation tag on cached item entries** — The `type_{codename}` cache dependency tag — previously attached only to cached type-definition responses from `GetType()` / `GetTypes()` — is now also attached to every cached item / item-list response whose payload references at least one item of that content type (including transitive references via modular content, linked-items elements, and rich-text inline items). Invalidating `type_{codename}` therefore evicts **both** the cached type definition and any item caches referencing items of that type, giving consumers a granular invalidation signal for content-type webhooks without resorting to the coarse `DeliveryCacheDependencies.ItemsListScope`.

  Runtime behavior:
  - Cached `GetItem<T>()` and `GetItems<T>()` responses now carry an additional `type_{codename}` dependency key for every content type referenced by the response (including the primary item(s) and every entry in `ModularContent`).
  - These keys are surfaced via `IDeliveryResult<T>.DependencyKeys` alongside existing `item_{codename}`, `asset_{guid}`, and `taxonomy_{group}` keys, so downstream tag-based caches (e.g. ASP.NET output cache) automatically benefit as well.
  - No change to cache-key format, storage, or expiration — only the set of tags attached to each entry grows.

  Recommended webhook pattern for content-type change or deletion events:
  ```csharp
  await cacheManager.InvalidateAsync([
      $"type_{typeCodename}",
      DeliveryCacheDependencies.TypesListScope]);
  ```
  This single call now invalidates the cached type definition, every cached item / item-list response containing items of that type, and the types-list cache.

  The change is purely additive inside `DependencyTrackingContext` (internal) and the item-query builders. XML documentation on `IDeliveryCacheManager` has been updated to describe the broadened semantics of `type_{codename}`.

#### Documentation updates

- `docs/upgrade-guide.md` — Comprehensive 18.x → 19.0 migration guide covering query methods, filtering, caching, resilience, rich text, response handling, model structure, DI registration, removed features, new features, and Sync API migration.
- `docs/caching-guide.md` — Updated dependency-tracking section, invalidation matrix, and webhook recipe to describe the broadened `type_{codename}` semantics.

#### Migration from 18.x

See [`docs/upgrade-guide.md`](docs/upgrade-guide.md). Every call site that uses the SDK will need code changes — the guide is structured as an eleven-section walkthrough with before/after snippets for each area, plus a quick checklist.

#### Migration from RC5

No code changes required for the RC5 → GA delta. Existing cache-invalidation calls keep working; consumers can opt into the finer-grained type-invalidation behavior by switching from `DeliveryCacheDependencies.ItemsListScope` to `type_{codename}` where the coarse scope was used as a workaround for missing per-type invalidation.

If you are catching the RC line up to GA, also review the cumulative changes introduced across the pre-release cycle via the per-RC changelogs linked below.

---

See [rc5 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc5), [rc4 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc4), [rc3 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc3), [rc2 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc2), and [rc1 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc1) for per-RC changelogs.


### What's Changed
* Modernization by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/407


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/18.3.0...19.0.0

## 19.0.0-rc5 (2026-04-13)  _(prerelease)_

`InvalidateAsync` parameter reorder for idiomatic .NET convention, `ContinuationToken` removed from `IDeliveryResult<T>`, `IError.ErrorCode` semantic fix, `TryGet` added to `IDeliveryClientFactory`, rich text whitespace fix, and internal cache manager simplifications.

#### New

- **`IDeliveryClientFactory.TryGet(string name)`** — Returns the named client, or `null` if no client with that name has been registered. Use this instead of catching `InvalidOperationException` from `Get` when the client's presence is conditional.

#### Breaking changes
- **`InvalidateAsync` parameter order changed** — `IDeliveryCacheManager.InvalidateAsync` signature changed from `(CancellationToken, params string[])` to `(string[], CancellationToken)`. The `CancellationToken` now follows standard .NET convention as the last parameter. The `params` keyword was removed; use collection expressions instead. If you implement a custom `IDeliveryCacheManager`, update the parameter order.
- **`IDeliveryResult<T>.ContinuationToken` removed** — The property was always `null` except for items-feed responses, where it was an internal HTTP protocol detail (`X-Continuation` header) that leaked into the public contract. Feed pagination is fully handled by `IDeliveryItemsFeedResponse.HasNextPage` / `FetchNextPageAsync()`. If you implement or mock `IDeliveryResult<T>`, remove the `ContinuationToken` member. If you read `result.ContinuationToken` at call sites, remove those reads and rely on the feed response interface instead.
- **`IError.ErrorCode` no longer populated for non-Kontent.ai errors** — Previously, when an error could not be parsed as a structured Kontent.ai API error (network failure, non-JSON response body, empty body), `ErrorCode` was set to the HTTP status code integer as a fallback. This conflated two different concepts: Kontent.ai application error codes and HTTP status codes. `ErrorCode` is now `null` in those cases. The HTTP status is already available on `IDeliveryResult.StatusCode`. If your error-handling code checks `error.ErrorCode` expecting the HTTP status for non-API errors, switch to `result.StatusCode`.

#### Bug fixes
- **Rich text: whitespace between adjacent inline elements preserved** — A single space (or any whitespace-only text node) that separates two inline elements — e.g. `</a> <strong>` or `</strong> <em>` — was silently dropped during rich text parsing. The rendered HTML was missing those spaces, causing words to run together. The parser now retains whitespace-only text nodes so the resolved HTML matches the original content.

#### Internal improvements
- **Extracted shared cache policy helper in `FusionCacheManager`** — Fail-safe, jitter, and eager-refresh option configuration is now applied via a single `ApplyCachePolicy` helper, eliminating duplication across `CreateMemory` and `CreateHybrid` factory methods.
- **Removed dead `_ownsFusionCache` field** — Both factory methods always owned the `IFusionCache` instance. The conditional dispose branch was unreachable and has been removed.
- **Reduced allocation in `InvalidateAsync`** — The common path (all dependency keys valid) no longer allocates an intermediate array from LINQ filtering.

#### Migration from RC4

1. Update `InvalidateAsync` call sites — move `CancellationToken` to the last argument and wrap string arguments in collection expressions:
   ```csharp
   // Before (RC4)
   await cacheManager.InvalidateAsync(default, "item_hero");
   await cacheManager.InvalidateAsync(default, "item_hero", "item_author");
   await cacheManager.InvalidateAsync(stoppingToken, "items_news");

   // After (RC5)
   await cacheManager.InvalidateAsync(["item_hero"]);
   await cacheManager.InvalidateAsync(["item_hero", "item_author"]);
   await cacheManager.InvalidateAsync(["items_news"], stoppingToken);
   ```

2. If you implement `IDeliveryCacheManager`, update the `InvalidateAsync` signature:
   ```csharp
   // Before (RC4)
   public Task<bool> InvalidateAsync(CancellationToken cancellationToken = default, params string[] dependencyKeys)

   // After (RC5)
   public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
   ```

3. If you pass a `string[]` variable directly, the call simplifies (no `default` needed):
   ```csharp
   // Before (RC4)
   await cacheManager.InvalidateAsync(default, dependencyKeys);

   // After (RC5)
   await cacheManager.InvalidateAsync(dependencyKeys);
   ```

4. Remove `ContinuationToken` from any `IDeliveryResult<T>` implementations or mocks:
   ```csharp
   // Before (RC4) — implementing or mocking IDeliveryResult<T>
   public string? ContinuationToken => null;

   // After (RC5) — remove the member entirely
   ```
   If you read `result.ContinuationToken` to drive feed pagination, use the feed response instead:
   ```csharp
   // Before (RC4)
   while (!string.IsNullOrEmpty(result.ContinuationToken)) { ... }

   // After (RC5)
   while (result.Value.HasNextPage)
   {
       result = await result.Value.FetchNextPageAsync();
   }
   ```

5. If you check `error.ErrorCode` expecting the HTTP status code for transport-level or non-JSON errors, switch to `result.StatusCode`:
   ```csharp
   // Before (RC4) — ErrorCode was set to HTTP status for non-Kontent.ai errors
   if (result.Error?.ErrorCode == 503) { ... }

   // After (RC5) — use StatusCode; ErrorCode is null for non-structured errors
   if (result.StatusCode == HttpStatusCode.ServiceUnavailable) { ... }
   ```

See [rc4 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc4), [rc3 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc3), [rc2 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc2), and [rc1 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc1) for previous changelogs.


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.0.0-rc4...19.0.0-rc5

## 19.0.0-rc4 (2026-03-18)  _(prerelease)_

Dependency keys on all delivery results, cache envelope architecture, and `CacheResult<T>` to preserve dependency metadata through the cache boundary.

#### Breaking changes
- **`IDeliveryCacheManager.GetOrSetAsync<T>` return type changed** — Return type changed from `Task<T?>` to `Task<CacheResult<T>?>`. The new `CacheResult<T>` record carries both the cached value (`Value`) and its canonical dependency keys (`DependencyKeys`). If you implement a custom `IDeliveryCacheManager`, update the return type and wrap values in `CacheResult<T>`. Built-in `MemoryCacheManager` and `HybridCacheManager` are updated automatically.
- **`IDeliveryResult<T>` now includes `DependencyKeys` property** — A new `IReadOnlyList<string>? DependencyKeys` property has been added to `IDeliveryResult<T>`. Returns `null` when dependency keys were not collected, or a list of canonical keys when available. If you manually implement or mock `IDeliveryResult<T>`, add the new member (return `null` for no-op implementations).

#### New features
- **Dependency keys exposed on all delivery results** — Every `IDeliveryResult<T>` now carries the canonical dependency keys describing which content entities the response depends on (e.g., `item_hero`, `asset_a5e1c4b2`, `taxonomy_personas`). These keys enable downstream cache invalidation scenarios such as ASP.NET output-cache tagging, CDN surrogate keys, or any external cache that needs content-aware invalidation. Access via `result.DependencyKeys` (`null` when not collected, non-null list when available).
- **`CacheResult<T>` type** — New public record `CacheResult<T>(T Value, IReadOnlyList<string> DependencyKeys)` returned by `IDeliveryCacheManager.GetOrSetAsync<T>`. Preserves dependency keys through the cache boundary so they can be surfaced on delivery results even for cache hits.

#### Internal improvements
- **Dependency extraction always active** — `ContentDependencyExtractor` is now always registered (previously a no-op `NullContentDependencyExtractor` was used when caching was disabled). This enables dependency keys on results regardless of cache configuration.
- **Cache envelope architecture** — `FusionCacheManager` now stores values in an internal `CacheEnvelope<T>` that preserves dependency keys alongside the cached value, eliminating the need for query builders to manually wrap/unwrap dependency metadata.

#### Migration from RC3

1. If you implement `IDeliveryCacheManager`, update `GetOrSetAsync` return type:
   ```csharp
   // Before
   public async Task<T?> GetOrSetAsync<T>(...) where T : class
   {
       var entry = await factory(cancellationToken);
       return entry?.Value;
   }

   // After
   public async Task<CacheResult<T>?> GetOrSetAsync<T>(...) where T : class
   {
       var entry = await factory(cancellationToken);
       if (entry is null) return null;
       return new CacheResult<T>(entry.Value, entry.Dependencies.ToArray());
   }
   ```

2. If you implement or mock `IDeliveryResult<T>`, add the new `DependencyKeys` property:
   ```csharp
   public IReadOnlyList<string>? DependencyKeys => null;
   ```

3. If you read properties from the cache manager return value, access `.Value` first:
   ```csharp
   // Before
   var cached = await cacheManager.GetOrSetAsync("key", factory);
   if (cached != null) DoSomething(cached.SomeProperty);

   // After
   var cached = await cacheManager.GetOrSetAsync("key", factory);
   if (cached != null) DoSomething(cached.Value.SomeProperty);
   ```

See [rc3 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc3), [rc2 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc2), and [rc1 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc1) for previous changelogs.


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.0.0-rc3...19.0.0-rc4

## 19.0.0-rc3 (2026-03-06)  _(prerelease)_

> [!NOTE]
> RC3 includes all changes from RC2 that were not included in the RC2 package due to a packaging error, plus one additional naming change.

Response provenance metadata, asset nullability improvements, hybrid cache rename, and reliability fixes across caching, query serialization, and rich text processing.

#### Breaking changes
- **`EnumerateItemsAsync` renamed to `EnumerateAsync`** — The terminal method on `IEnumerateItemsQuery<TModel>`, `IDynamicEnumerateItemsQuery`, `IItemUsedInQuery`, and `IAssetUsedInQuery` has been renamed from `EnumerateItemsAsync` to `EnumerateAsync`. The "Items" suffix was redundant given the query context. Find-and-replace `.EnumerateItemsAsync(` → `.EnumerateAsync(` in your code.
- **Response source metadata on delivery results** — `IDeliveryResult<T>` now includes a `ResponseSource` property (`Origin`, `Cdn`, `Cache`, `FailSafe`) to distinguish API/CDN responses from SDK cache and fail-safe responses. `IsCacheHit` remains available and maps to `Cache` or `FailSafe`. If you manually implement or mock `IDeliveryResult<T>`, add the new `ResponseSource` member.
- **Asset width/height are now nullable** — `IAsset.Width` and `IAsset.Height` changed from `int` to `int?`. Missing or non-numeric values now map to `null` instead of defaulting to `0`. Add null checks if your code assumes these fields are always present.
- **`DistributedCacheManager` renamed to `HybridCacheManager`** — Public DI extension methods renamed to better reflect the hybrid (L1 memory + L2 distributed) caching behavior:
  | Before | After |
  |---|---|
  | `AddDeliveryDistributedCache()` | `AddDeliveryHybridCache()` |
  | `WithDistributedCache()` | `WithHybridCache()` |

  Microsoft's `IDistributedCache`, `AddDistributedMemoryCache`, `AddStackExchangeRedisCache`, etc. are **not** affected.
- **`InvalidateAsync` returns `Task<bool>`** — `IDeliveryCacheManager.InvalidateAsync` now returns `Task<bool>` instead of `Task`. Returns `true` on success, `false` on failure. Exceptions are still swallowed and logged — callers who don't check the return value get fire-and-forget behavior; callers who care can check and retry.
- **`ConfigureFusionCacheOptions` callback** — New `Action<object>? ConfigureFusionCacheOptions` property on `DeliveryCacheOptions` allows advanced FusionCache configuration (backplane, eager refresh, etc.) without leaking FusionCache types into the Abstractions API. The callback receives the `FusionCacheOptions` instance after SDK defaults are applied.

#### New features
- **`WithoutElements` on feed queries** — `IEnumerateItemsQuery<TModel>` and `IDynamicEnumerateItemsQuery` now expose `WithoutElements(params string[] elementCodenames)` to exclude elements from feed responses, matching the existing method on item/items queries.

#### Fixed
- **Elements projection serialized as repeated query parameters** — `.WithElements()` and `.WithoutElements()` produced multiple `elements=` / `excludeElements=` query parameters (e.g., `elements=title&elements=summary`) instead of a single comma-delimited value (`elements=title,summary`). The API ignored the repeated keys, effectively applying only the last value.
- **`_failSafeActiveKeys` leak in long-running services** — `FusionCacheManager` now cleans up `_failSafeActiveKeys` entries when FusionCache evicts them, preventing unbounded memory growth.
- **`PurgeAsync` incorrectly cleared fail-safe tracking** — `PurgeAsync` now only clears `_failSafeActiveKeys` when `allowFailSafe == false`. Previously it cleared unconditionally, causing stale entries to report `ResponseSource.Cache` instead of `ResponseSource.FailSafe` until the next hit.
- **Rich text void element handling scoped to Kontent.ai spec** — `HtmlElementResolver` now only treats `<br>` and `<img>` as void elements, matching the [Kontent.ai rich text allowed elements](https://kontent.ai/learn/docs/apis/delivery-api/content-elements#html-5-elements-allowed-in-rich-text). Previously used a per-call `HashSet` with 14 HTML5 void elements.
- **`IDeliveryResult<T>.Value` behavior documented** — Added XML doc specifying that `Value` may be `default` when `IsSuccess` is `false`.

#### Migration from RC2

1. Find-and-replace in your startup/DI code:
   - `AddDeliveryDistributedCache` → `AddDeliveryHybridCache`
   - `WithDistributedCache` → `WithHybridCache`

2. If your code reads `IAsset.Width` or `IAsset.Height`, add null checks:
   ```csharp
   // Before
   var width = asset.Width;

   // After
   if (asset.Width is not null)
   {
       var width = asset.Width.Value;
   }
   ```

3. If you implement or mock `IDeliveryResult<T>`, add the new `ResponseSource` member.

4. If you implement `IDeliveryCacheManager`, update `InvalidateAsync` to return `bool`.

5. Find-and-replace `.EnumerateItemsAsync(` → `.EnumerateAsync(` in any code using feed or used-in queries.

See [rc2 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc2) and [rc1 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc1) for previous changelogs.

## 19.0.0-rc2 (2026-03-04)  _(prerelease)_

Response provenance metadata, asset nullability improvements, hybrid cache rename, and reliability fixes across caching, query serialization, and rich text processing.

#### Breaking changes
- **Response source metadata on delivery results** — `IDeliveryResult<T>` now includes a `ResponseSource` property (`Origin`, `Cdn`, `Cache`, `FailSafe`) to distinguish API/CDN responses from SDK cache and fail-safe responses. `IsCacheHit` remains available and maps to `Cache` or `FailSafe`. If you manually implement or mock `IDeliveryResult<T>`, add the new `ResponseSource` member.
- **Asset width/height are now nullable** — `IAsset.Width` and `IAsset.Height` changed from `int` to `int?`. Missing or non-numeric values now map to `null` instead of defaulting to `0`. Add null checks if your code assumes these fields are always present.
- **`DistributedCacheManager` renamed to `HybridCacheManager`** — Public DI extension methods renamed to better reflect the hybrid (L1 memory + L2 distributed) caching behavior:
  | Before | After |
  |---|---|
  | `AddDeliveryDistributedCache()` | `AddDeliveryHybridCache()` |
  | `WithDistributedCache()` | `WithHybridCache()` |

  Microsoft's `IDistributedCache`, `AddDistributedMemoryCache`, `AddStackExchangeRedisCache`, etc. are **not** affected.
- **`InvalidateAsync` returns `Task<bool>`** — `IDeliveryCacheManager.InvalidateAsync` now returns `Task<bool>` instead of `Task`. Returns `true` on success, `false` on failure. Exceptions are still swallowed and logged — callers who don't check the return value get fire-and-forget behavior; callers who care can check and retry.
- **`ConfigureFusionCacheOptions` callback** — New `Action<object>? ConfigureFusionCacheOptions` property on `DeliveryCacheOptions` allows advanced FusionCache configuration (backplane, eager refresh, etc.) without leaking FusionCache types into the Abstractions API. The callback receives the `FusionCacheOptions` instance after SDK defaults are applied.

#### New features
- **`WithoutElements` on feed queries** — `IEnumerateItemsQuery<TModel>` and `IDynamicEnumerateItemsQuery` now expose `WithoutElements(params string[] elementCodenames)` to exclude elements from feed responses, matching the existing method on item/items queries.

#### Fixed
- **Elements projection serialized as repeated query parameters** — `.WithElements()` and `.WithoutElements()` produced multiple `elements=` / `excludeElements=` query parameters (e.g., `elements=title&elements=summary`) instead of a single comma-delimited value (`elements=title,summary`). The API ignored the repeated keys, effectively applying only the last value.
- **`_failSafeActiveKeys` leak in long-running services** — `FusionCacheManager` now cleans up `_failSafeActiveKeys` entries when FusionCache evicts them, preventing unbounded memory growth.
- **`PurgeAsync` incorrectly cleared fail-safe tracking** — `PurgeAsync` now only clears `_failSafeActiveKeys` when `allowFailSafe == false`. Previously it cleared unconditionally, causing stale entries to report `ResponseSource.Cache` instead of `ResponseSource.FailSafe` until the next hit.
- **Rich text void element handling scoped to Kontent.ai spec** — `HtmlElementResolver` now only treats `<br>` and `<img>` as void elements, matching the [Kontent.ai rich text allowed elements](https://kontent.ai/learn/docs/apis/delivery-api/content-elements#html-5-elements-allowed-in-rich-text). Previously used a per-call `HashSet` with 14 HTML5 void elements.
- **`IDeliveryResult<T>.Value` behavior documented** — Added XML doc specifying that `Value` may be `default` when `IsSuccess` is `false`.

#### Migration from RC1

1. Find-and-replace in your startup/DI code:
   - `AddDeliveryDistributedCache` → `AddDeliveryHybridCache`
   - `WithDistributedCache` → `WithHybridCache`

2. If your code reads `IAsset.Width` or `IAsset.Height`, add null checks:
   ```csharp
   // Before
   var width = asset.Width;

   // After
   if (asset.Width is not null)
   {
       var width = asset.Width.Value;
   }
   ```

3. If you implement or mock `IDeliveryResult<T>`, add the new `ResponseSource` member.

4. If you implement `IDeliveryCacheManager`, update `InvalidateAsync` to return `bool`.

See [rc1 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0-rc1) for previous changelog.

## 19.0.0-rc1 (2026-02-20)  _(prerelease)_

Attribute-based type resolution with source generation, plus performance and reliability improvements across query execution, caching, and content item processing.

#### Breaking changes
- Renamed `ITypeProvider.TryGetModelType()` to `ITypeProvider.GetType()` for naming consistency with `GetCodename()`
- Removed the need for manual `CustomTypeProvider` - use the source-generated `GeneratedTypeProvider` instead (reference the sourcegen package and it will be generated automatically from models registered in your project)
- `RequiredIfAttribute` is now `sealed` (custom inheritance from this attribute is no longer supported)
- `IRichTextElementValue` metadata collections are now read-only (`IReadOnlyDictionary` / `IReadOnlyList` instead of mutable `IDictionary` / `List`)
- `DeliveryOptionsBuilder.WithCustomEndpoint(...)` now applies the endpoint to both `ProductionEndpoint` and `PreviewEndpoint` (previously it only affected the API mode active at call time)
- Removed `Options.DefaultName`; use `IDeliveryClientFactory.Get()` for the default client (or `"Default"` when explicitly resolving keyed services)
- Removed `DeliveryOptions.IncludeTotalCount` - this global option was superseded by the per-query `.WithTotalCount()` method on `IItemsQuery<T>` / `IDynamicItemsQuery`, which is now the only way to include total count
- `IDeliveryCacheManager.GetAsync<T>` and `SetAsync<T>` replaced with a single factory-based `GetOrSetAsync<T>(cacheKey, factory, expiration?, cancellationToken)` method. Custom implementations must update to the new interface. The factory receives a `CancellationToken` and returns `CacheEntry<T>?` (null signals "don't cache").
- **Caching extracted to standalone package** - FusionCache-backed caching implementations (`MemoryCacheManager`, `DistributedCacheManager`) moved from `Kontent.Ai.Delivery` to a new `Kontent.Ai.Delivery.Caching` package. The core `Kontent.Ai.Delivery` package no longer depends on `ZiggyCreatures.FusionCache`. Users who enable caching must add `Kontent.Ai.Delivery.Caching` as a package reference.
- `DeliveryClientBuilder.WithMemoryCache()` and `.WithDistributedCache()` moved to extension methods in `Kontent.Ai.Delivery.Caching`. Add a reference to the caching package to restore these APIs.
- `AddDeliveryMemoryCache()`, `AddDeliveryDistributedCache()`, and `AddDeliveryCacheManager()` DI extension methods moved to `ServiceCollectionExtensions` in `Kontent.Ai.Delivery.Caching`.
- `DeliveryClientBuilder` gains a general-purpose `ConfigureServices(Action<IServiceCollection>)` method for extensibility.
- `IHtmlResolver` now extends `IRichTextResolver<string>`. Existing implementations are source-compatible — no code changes needed.

#### New features
- **Attribute-based type registration** - Model classes generated by the [Kontent.ai Model Generator](https://github.com/kontent-ai/model-generator-net) include the `[ContentTypeCodename("codename")]` attribute:
  ```csharp
  // Generated by Kontent.ai Model Generator
  using Kontent.Ai.Delivery.Attributes;

  [ContentTypeCodename("article")]
  public class Article
  {
      public string? Title { get; set; }
      // ...
  }
  ```

- **Source-generated type provider** - The new `Kontent.Ai.Delivery.SourceGeneration` package automatically generates a `GeneratedTypeProvider : ITypeProvider` at compile time. The SDK auto-discovers it at runtime - no manual registration needed.

- **Build-time type metadata generation** - During compilation, `Kontent.Ai.Delivery.SourceGeneration` emits `ContentTypeCodenameAttribute` and generates `GeneratedTypeProvider` from your attributed model types.

- **Compile-time diagnostics** for content type attributes:
  - `KDSG001`: Duplicate codename (error)
  - `KDSG002`: Invalid codename - null, empty, or whitespace (error)
  - `KDSG003`: Unsupported target type - interfaces and abstract classes (error)

- **Automatic `system.type` filter** - Generic queries like `GetItems<Article>()` automatically add `system.type=article` filter when the type is registered in the type provider.

- **FusionCache-native stampede protection** - The atomic `GetOrSetAsync` factory pattern provides built-in stampede protection: concurrent cache misses for the same key coalesce into a single factory invocation with no custom locking code.

- **Eager refresh** - New `EagerRefreshThreshold` option in `DeliveryCacheOptions` triggers proactive background refresh before cache entries expire (e.g., set to `0.8f` to refresh at 80% of TTL).

- **Simplified BYO cache** - `IDeliveryCacheManager` now has a single `GetOrSetAsync` method, making custom cache implementations trivial (~20 lines).

- **Synthetic item-list cache scope invalidation** - Cached typed item-list queries (`GetItems<T>()`) now include `DeliveryCacheDependencies.ItemsListScope` (`scope_items_list`) as a dependency key. This enables webhook handlers to invalidate all cached typed item-list queries in one call, fixing stale list membership scenarios when new/updated items start matching existing filters.

- **Safer webhook invalidation pattern for lists** - For item events, invalidate both item-specific keys (`item_{codename}`) and `DeliveryCacheDependencies.ItemsListScope` to refresh both detail and listing caches without a full purge.

- **Type/taxonomy cache dependencies and list scopes** - Cached type queries now include direct dependencies (`type_{codename}` for `GetType()`, `taxonomy_{codename}` for `GetTaxonomy()`), and cached listing queries include synthetic scope dependencies (`DeliveryCacheDependencies.TypesListScope` / `DeliveryCacheDependencies.TaxonomiesListScope`).

- **Expanded webhook invalidation pattern for schema/taxonomy changes** - Type and taxonomy events can now invalidate both direct entity keys and list scopes (`type_{codename}` + `scope_types_list`, `taxonomy_{codename}` + `scope_taxonomies_list`) for consistent refresh behavior without full cache purge.

- **Per-query cache expiration override** - Cacheable query builders now support `.WithCacheExpiration(...)` to override TTL for a specific request while keeping manager-level defaults unchanged.

- **Safer typed memory-cache key isolation** - Hydrated-object item and item-list cache keys now include a model discriminator, preventing different generic model types from evicting each other on the same query key.

- **Improved cancellation behavior in distributed invalidation** - `InvalidateAsync` now propagates cancellation reliably through dependency invalidation work.

- **Cancellation propagation** - All Delivery API calls now accept and propagate `CancellationToken` through query builders to the underlying HTTP requests.

- **Simplified processing pipeline** - Converters now create minimal content item shells and the `ContentItemMapper` performs all element mapping (simple + complex) during post-processing.

- **Allocation reductions in processing**:
  - Avoids redundant JSON round-trips by deserializing from `JsonElement` where possible (`IContentDeserializer` supports a `JsonElement` overload).
  - Avoids re-parsing JSON when cloning item/envelope elements by using `JsonElement.Clone()`.

- **Faster property assignment during mapping** - Replaces reflection-based `PropertyInfo.SetValue(...)` with cached compiled setters for improved throughput when mapping large item listings.

- **Rich text hardening** - Adds a max parsing depth guard to prevent stack overflow on deeply nested HTML and improves null-safety for inline images.

- **Dynamic mode rich text parsing** - New `ParseRichTextAsync` extension method on `JsonElement` enables rich text resolution when using dynamic content access. See [Dynamic Mode Resolution](docs/rich-text-customization.md#dynamic-mode-resolution) in the Rich Text Customization Guide.

- **Generic rich text resolver interface** — New `IRichTextResolver<TOutput>` base interface enables custom rich text resolution to any output format (Markdown, portable text, view models, etc.). `IHtmlResolver` extends `IRichTextResolver<string>` for HTML output.

- **Diagnostic logging for silent failures** - Adds Debug-level logging when API error parsing or cache deserialization fail silently, improving production incident diagnosis without impacting normal operation.

#### Fixed
- **Filter values with special characters now work correctly** - Values containing spaces, ampersands (`&`), and other URL-sensitive characters were being double-encoded, causing filters to silently return no results. Filters with only alphanumeric characters, hyphens, and underscores were unaffected.
- **Named-client rendition preset consistency** - `DefaultRenditionPreset` is now applied per client configuration across item/list/feed mapping paths (including dynamic runtime typing and cache rehydration), preventing cross-client preset leakage in multi-client setups.
- **Corrupted modular cache payloads now fail fast instead of partially hydrating linked items** - If modular content JSON in a cached raw payload is malformed, rehydration is treated as cache corruption and falls back to a fresh API fetch path rather than returning a partially completed result.
- **Consistent query lifecycle logging for item queries** - `ItemQuery` and `ItemsQuery` now emit `QueryCompleted` on failure paths in addition to `QueryFailed`, keeping telemetry lifecycle events consistent across success/failure outcomes.

#### RC1 consistency note (distributed cache)
- **Distributed invalidation is now deterministic** - FusionCache tag-based invalidation replaces the former reverse-index approach, eliminating race conditions under concurrent writes. All entries sharing a dependency tag are invalidated atomically.
- **Stale-window guidance** - keep distributed cache TTLs reasonably short (for example, less than one hour) to bound stale exposure in scenarios where backplane propagation is delayed.

#### Internal refactors (no public API changes)
- Aligned remaining query builders (`TypeQuery`, `TypesQuery`, `TypeElementQuery`, `TaxonomyQuery`, `TaxonomiesQuery`, `LanguagesQuery`) to the same internal execution structure used by item/list/feed queries for easier maintenance and more consistent pagination wiring.
- Introduced shared internal helpers for delivery result mapping and offset pagination skip calculation to reduce duplication across query builders.
- Consolidated `ItemUsedInQuery` and `AssetUsedInQuery` pagination loops into a shared internal core while preserving runtime behavior (including graceful stop on failed intermediate pages).

#### New packages
- `Kontent.Ai.Delivery.SourceGeneration` - Roslyn incremental source generator that creates `GeneratedTypeProvider` and emits `ContentTypeCodenameAttribute` for marking model classes.
- `Kontent.Ai.Delivery.Caching` - FusionCache-backed caching implementations (`MemoryCacheManager`, `DistributedCacheManager`) extracted from the core SDK package. Add this package if you use caching.

#### Migration from beta-5

1. If you use `AddDeliveryMemoryCache`, `AddDeliveryDistributedCache`, `AddDeliveryCacheManager`, or `DeliveryClientBuilder.WithMemoryCache()`/`.WithDistributedCache()`, add a reference to the caching package:
   ```xml
   <PackageReference Include="Kontent.Ai.Delivery.Caching" Version="19.0.0-rc1" />
   ```

2. Add package reference for source generation:
   ```xml
   <PackageReference Include="Kontent.Ai.Delivery.SourceGeneration" Version="19.0.0-rc1" />
   ```

3. Regenerate your models using the [Kontent.ai Model Generator](https://github.com/kontent-ai/model-generator-net) - the generated models now include `[ContentTypeCodename]` attributes automatically.

4. Ensure `Kontent.Ai.Delivery.SourceGeneration` is referenced by the project that compiles the generated model `.cs` files (for example your `Models` class library). The attribute definition and provider are generated during that build.

5. If you implemented custom `ITypeProvider`, rename `TryGetModelType` to `GetType`.

6. If you implemented a custom `IContentDeserializer`, consider overriding `DeserializeContentItem(JsonElement, Type)` to avoid `GetRawText()` allocations (a default implementation is provided for compatibility).

7. If you implemented a custom `IDeliveryCacheManager`, replace `GetAsync<T>` + `SetAsync<T>` with the new `GetOrSetAsync<T>(cacheKey, factory, expiration?, cancellationToken)` method. The factory returns `CacheEntry<T>?` — return `null` to signal "don't cache" (e.g., API failure). Example:
   ```csharp
   public async Task<T?> GetOrSetAsync<T>(
       string cacheKey,
       Func<CancellationToken, Task<CacheEntry<T>?>> factory,
       TimeSpan? expiration = null,
       CancellationToken cancellationToken = default) where T : class
   {
       if (_cache.TryGetValue(cacheKey, out T cached)) return cached;
       var entry = await factory(cancellationToken);
       if (entry is null) return null;
       _cache.Set(cacheKey, entry.Value, expiration);
       return entry.Value;
   }
   ```

That's it! The SDK auto-discovers the generated type provider at runtime.

See [beta-5 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/v19.0.0-beta-5) for previous changelog.

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.0.0-beta-4...19.0.0-rc1

## 19.0.0-beta-5 (2026-01-29)  _(prerelease)_

Runtime type resolution and API cleanup.

#### Breaking changes
- Removed `DeliveryClientBuilder.WithEnvironmentId` - use `WithOptions(opts => opts.WithEnvironmentId("...").UseProductionApi().Build())` instead
- Moved extension methods to `Kontent.Ai.Delivery` namespace - remove `using Kontent.Ai.Delivery.Extensions;` if present
- Changed dynamic query return types from `IContentItem<IDynamicElements>` to `IContentItem` to support runtime type resolution. Use pattern matching to access elements:
  ```csharp
  var result = await client.GetItem("codename").ExecuteAsync();
  if (result.Value is IContentItem<Article> article)
  {
      var title = article.Elements.Title;
  }
  ```

#### New features
- Runtime type resolution - typeless queries automatically resolve to strongly-typed models when `ITypeProvider` is registered
- Circular reference hydration - circular references now return the same object instance, creating proper object graph cycles
- New `AddDeliveryClient(IConfigurationSection)` overload for registering directly from a configuration section

See [beta-4 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/v19.0.0-beta-4) for previous changelog.


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/19.0.0-beta-4...19.0.0-beta-5

## 19.0.0-beta-4 (2026-01-11)  _(prerelease)_

Fixes NuGet package health issues from beta-3. No functional changes.

#### Package fixes
- Fixed Source Link configuration (symbols now properly embedded)
- Enabled deterministic builds for reproducibility
- Updated Microsoft.SourceLink.GitHub to 8.0.0
- Fixed license metadata not displaying in NuGet Package Explorer
- Updated logo

See [beta-3 release notes](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/v4.0.0-beta-3) for feature changelog.

## 19.0.0-beta-3 (2026-01-11)  _(prerelease)_

### New Features

#### DeliveryClientBuilder

Create `IDeliveryClient` instances without dependency injection:

```csharp
using var container = DeliveryClientBuilder
    .WithEnvironmentId("your-environment-id")
    .WithTypeProvider(new GeneratedTypeProvider())
    .WithMemoryCache(TimeSpan.FromMinutes(30))
    .Build();

var client = container.Client;
```

#### Logging Infrastructure

Source-generated `LoggerMessage` for high-performance logging throughout query execution, caching, HTTP operations, and content mapping.

#### Retry-After Header Support

The SDK now respects `Retry-After` headers for 429 responses.

#### DeliveryResult Metadata

- `IsCacheHit` - Indicates if response came from cache
- `ResponseHeaders` - Access to raw HTTP headers
- `HasStaleContent` - Indicates if newer content may be available

#### Cache Purge Support

New `IDeliveryCachePurger` interface for purging all memory cache entries.

#### Language Fallback Control

New `LanguageFallbackMode` parameter for `WithLanguage()`:

```csharp
// Disable language fallbacks
.WithLanguage("es-ES", LanguageFallbackMode.Disabled)
```

---

### Breaking Changes

#### Filtering API Redesign

The entire filtering system was replaced with a new type-safe DSL.

**Method renaming:**

| Beta-2 | Beta-3 |
|--------|--------|
| `.Filter(f => ...)` | `.Where(f => ...)` |

**Path construction:**

| Beta-2 | Beta-3 |
|--------|--------|
| `ItemSystemPath.Type` | `f.System("type")` |
| `ItemSystemPath.Codename` | `f.System("codename")` |
| `ItemSystemPath.LastModified` | `f.System("last_modified")` |
| `Elements.GetPath("title")` | `f.Element("title")` |

**Operator method renaming:**

| Beta-2 | Beta-3 |
|--------|--------|
| `f.Equals(path, value)` | `f.System("...").IsEqualTo(value)` |
| `f.NotEquals(path, value)` | `f.System("...").IsNotEqualTo(value)` |
| `f.GreaterThan(path, value)` | `f.Element("...").IsGreaterThan(value)` |
| `f.LessThan(path, value)` | `f.Element("...").IsLessThan(value)` |
| `f.LessThanOrEqual(path, value)` | `f.Element("...").IsLessThanOrEqualTo(value)` |
| `f.GreaterThanOrEqual(path, value)` | `f.Element("...").IsGreaterThanOrEqualTo(value)` |
| `f.Range(path, (low, high))` | `f.Element("...").IsWithinRange(low, high)` |
| `f.In(path, array)` | `f.System("...").IsIn("a", "b")` |
| `f.Any(path, values...)` | `f.Element("...").ContainsAny("a", "b")` |
| `f.All(path, values...)` | `f.Element("...").ContainsAll("a", "b")` |
| `f.Contains(path, value)` | `f.Element("...").Contains(value)` |
| `f.NotEmpty(path)` | `f.Element("...").IsNotEmpty()` |

**Full example:**

```csharp
// Beta-2
var result = await client.GetItems()
    .Filter(f => f.Equals(ItemSystemPath.Type, "article"))
    .Filter(f => f.Contains(Elements.GetPath("category"), "coffee"))
    .OrderBy(ItemSystemPath.LastModified, descending: true)
    .ExecuteAsync();

// Beta-3
var result = await client.GetItems()
    .Where(f => f
        .System("type").IsEqualTo("article")
        .Element("category").Contains("coffee"))
    .OrderBy("system.last_modified", OrderingMode.Descending)
    .ExecuteAsync();
```

**Removed filtering types:**
- `Filter` class (now internal)
- `FilterOperator` enum
- `ItemSystemPath` class
- `Elements.GetPath()` method
- `StringValue`, `IFilterValue` interfaces

#### Ordering API

```csharp
// Beta-2
.OrderBy(ItemSystemPath.LastModified, descending: true)

// Beta-3
.OrderBy("system.last_modified", OrderingMode.Descending)
```

#### Pagination / Feed API

```csharp
// Beta-2
var query = client.GetItemsFeed()
    .OrderBy(ItemSystemPath.LastModified, true);
await foreach (var item in query.ExecuteAsync())

// Beta-3
await foreach (var item in client.GetItemsFeed().EnumerateItemsAsync())
```

#### Caching Registration

Fluent chaining removed in favor of separate extension methods:

```csharp
// Beta-2
services.AddDeliveryClient(options => { ... })
    .WithMemoryCache(defaultExpiration: TimeSpan.FromHours(1));

// Beta-3
services.AddDeliveryClient(options => { ... });
services.AddDeliveryMemoryCache(defaultExpiration: TimeSpan.FromHours(1));
```

#### IEmbeddedContent Metadata Access

Metadata properties moved under `.System`:

```csharp
// Beta-2
linkedItem.ContentTypeCodename
linkedItem.Codename
linkedItem.Name
linkedItem.Id

// Beta-3
linkedItem.System.Type
linkedItem.System.Codename
linkedItem.System.Name
linkedItem.System.Id
```

#### Removed Types

| Type | Replacement |
|------|-------------|
| `IElementsPostProcessor` | Internal `ContentItemMapper` |
| `EmbeddedContentFactory` | Consolidated into content mapping |
| `IPropertyValueConverter` | Consolidated into mapper |
| `IElementsModel` | `IDynamicElements` |
| `ElementValue` | Removed |
| `DisableHtmlEncodeAttribute` | Removed |
| `UseDisplayTemplateAttribute` | Removed |
| `ItemSystemPath` | String paths with `f.System()` |
| `Elements.GetPath()` | `f.Element()` |

#### Query Builder Changes

- Removed `ExecuteAll()` helper methods from listing endpoints

---

### Improvements

#### Performance
- Cached `JsonSerializerOptions` across serialization
- Reduced reflection in hydration
- Lock striping (64 stripes) in `MemoryCacheManager`
- `FrozenDictionary` for immutable lookups

#### Caching
- Fixed unbounded cache lock growth
- Improved eviction logic
- Generation-safe entry overwrites

#### Security
- Max deserialization depth limit
- API keys never logged

#### Nullability
- Enabled throughout abstractions
- Fixed pagination `NextPage` nullability

---

### New Documentation Sections

- Content Types and Elements
- Taxonomies
- Reference Lookups (Used In)
- Dynamic Content Access
- Asset Renditions
- Image Transformation
- Error Handling
- Response Metadata

New guides:
- `extensibility-guide.md`
- `caching-guide.md`
- `rich-text-customization.md`

---

### Test Results

```
Passed: 563 | Failed: 0 | Skipped: 1
```

---

### Migration from beta-2

1. **Replace `.Filter()` with `.Where()`**

2. **Update filter path construction:**
   ```csharp
   // Old
   ItemSystemPath.Type → f.System("type")
   Elements.GetPath("title") → f.Element("title")
   ```

3. **Update filter operators:**
   ```csharp
   // Old
   f.Equals(path, value) → f.System("...").IsEqualTo(value)
   f.GreaterThan(path, value) → f.Element("...").IsGreaterThan(value)
   ```

4. **Update ordering:**
   ```csharp
   .OrderBy("system.last_modified", OrderingMode.Descending)
   ```

5. **Update pagination:**
   ```csharp
   await foreach (var item in client.GetItemsFeed().EnumerateItemsAsync())
   ```

6. **Update caching registration:**
   ```csharp
   services.AddDeliveryClient(options => { ... });
   services.AddDeliveryMemoryCache();
   ```

7. **Update IEmbeddedContent metadata access:**
   ```csharp
   linkedItem.System.Type  // was: linkedItem.ContentTypeCodename
   ```

8. **Remove references to deleted types** (`IElementsPostProcessor`, `FilterOperator`, `ItemSystemPath`, etc.)

---

Full Changelog: https://github.com/kontent-ai/delivery-sdk-net/compare/af74e1935355fc489c9e426950659a147a5f8437...vnext

## 19.0.0-beta-2 (2025-10-26)  _(prerelease)_

### Fixed: Strongly-Typed Linked Items Resolution

Linked items elements (modular content) are now automatically hydrated to strongly-typed embedded content, bringing the same developer experience as rich text embedded content to linked items elements.

  ### Fixed: Loosened accessors

`Filter` class was marked internal, preventing users from instantiating filters to be used in `.Where` filtering methods. Filter serialization moved to an internal extension method.

#### Fixed: Incorrect naming for types filtering method, missing examples in the docs

  ### What's New

  Previously, linked items were returned as simple string arrays (`IEnumerable<string>`) containing only codenames. Now they're fully hydrated to
  `IEnumerable<IEmbeddedContent>` with runtime type resolution, providing:

  - ✅ **Compile-time type safety** via pattern matching with `IEmbeddedContent<TModel>`
  - ✅ **Runtime type resolution** - each item gets its own type based on content type from the API
  - ✅ **Full metadata access** - codename, ID, content type, name for all items
  - ✅ **Mixed content types** - collections can contain different content types
  - ✅ **LINQ support** - filter by type using `.OfType<IEmbeddedContent<Article>>()`
  - ✅ **Consistent API** - same patterns as rich text embedded content

  ### Example Usage

  **Before:**
  ```csharp
  public record Article
  {
      [JsonPropertyName("related_articles")]
      public IEnumerable<string>? RelatedArticles { get; init; } // Just codenames
  }

  // Could only see codenames
  var codenames = article.RelatedArticles; // ["article_1", "article_2"]

  After:
  public record Article
  {
      [JsonPropertyName("related_articles")]
      public IEnumerable<IEmbeddedContent>? RelatedArticles { get; init; } // Fully hydrated
  }

  // Pattern matching for type-safe access
  foreach (var linkedItem in article.RelatedArticles!)
  {
      switch (linkedItem)
      {
          case IEmbeddedContent<Article> relatedArticle:
              Console.WriteLine($"Article: {relatedArticle.Elements.Title}");
              Console.WriteLine($"Summary: {relatedArticle.Elements.Summary}");
              break;

          case IEmbeddedContent<Product> product:
              Console.WriteLine($"Product: {product.Elements.Name}");
              Console.WriteLine($"Price: ${product.Elements.Price}");
              break;
      }
  }

  // LINQ filtering
  var articles = article.RelatedArticles!
      .OfType<IEmbeddedContent<Article>>()
      .ToList();

  // Extract models without wrapper
  var articleElements = article.RelatedArticles!
      .OfType<IEmbeddedContent<Article>>()
      .Select(a => a.Elements)
      .ToList();
```
#### Breaking Changes

  ⚠️ Model Updates Required: Linked items properties must be updated from IEnumerable<string> to IEnumerable<IEmbeddedContent>

#####  Migration:

```csharp
  // Old model
  public record Article
  {
      [JsonPropertyName("related_articles")]
      public IEnumerable<string>? RelatedArticles { get; init; }
  }

  // New model
  public record Article
  {
      [JsonPropertyName("related_articles")]
      public IEnumerable<IEmbeddedContent>? RelatedArticles { get; init; }
  }
```
  If you were previously accessing linked items as strings, update your code to use the new strongly-typed API shown in the examples above.

####  Technical Details

#####  Implementation:
  - Added EmbeddedContentFactory for shared reflection-based type construction
  - Extended ElementsPostProcessor to process modular_content elements
  - Updated IsComplexElementType to recognize modular_content as complex element
  - Refactored RichTextParser to use EmbeddedContentFactory (reduced code duplication)
  - Added comprehensive test coverage (9 new tests in StronglyTypedLinkedItemsTests)

#####  Files Changed:
  - Kontent.Ai.Delivery/Extensions/JsonElementExtensions.cs - Added modular_content support
  - Kontent.Ai.Delivery/ContentItems/Processing/EmbeddedContentFactory.cs - NEW - Shared factory
  - Kontent.Ai.Delivery/ContentItems/Processing/ElementsPostProcessor.cs - Linked items processing
  - Kontent.Ai.Delivery/ContentItems/Processing/RichTextParser.cs - Refactored to use factory
  - Test models updated (Article.cs, Home.cs, AboutUs.cs)
  - Documentation updated (README.md, ReadmeExamples.cs)

#####  Performance:
  - Parallel hydration using Task.WhenAll for efficient processing
  - Cached reflection constructors for optimal type construction
  - Integrated with existing dependency tracking for cache invalidation

#####  Documentation

  Updated documentation includes:
  - New section "Working with Linked Items" in README.md
  - Pattern matching examples
  - LINQ filtering examples
  - Mixed content type handling
  - Metadata access patterns
  - 5 new compiled examples in ReadmeExamples.cs

  ---
  Full Changelog: https://github.com/kontent-ai/delivery-sdk-net/compare/7afd7577cb475e33db6cf73eca18fbc188db4101...vnext

## 19.0.0-beta (2025-10-22)  _(prerelease)_

- this is a first beta release following the modernization effort
- a complete revamp of the architecture, aimed at improving the developer experience, reduce boilerplate and adopt modern .NET practices
- addresses majority of issues submitted over the years
- migration guide: TBD (the complexity of the overhaul inevitably led to a number of breaking changes. until a migration guide is finalized, see the readme for usage examples)
- model generator: TBD (generated models were simplified for testing purposes but the model generator hasn't been updated yet. inspect models such as Article.cs in the repository for an example model implementation)


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/18.3.0...19.0.0-beta

## 18.3.0 (2025-07-14)

### Features
* adds support for sync API v2 (more info in the [changelog](https://kontent.ai/learn/product-updates#a-introducing-sync-api-v2))

### Related PRs
* 404 sync api v2 by @sevcik-martin in https://github.com/kontent-ai/delivery-sdk-net/pull/405


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/18.2.0...18.3.0

## 18.2.0 (2025-04-09)

### Features
* adds support for newly introduced used in endpoints in form of `GetItemUsedIn` and `GetAssetUsedIn` methods, more info in the [product changelog](https://kontent.ai/learn/product-updates#a-discover-content-dependencies-with-new-delivery-api-endpoints)

### Related PRs
* Add support for used in endpoints by @winklertomas in https://github.com/kontent-ai/delivery-sdk-net/pull/403


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/18.1.1...18.2.0

## 18.1.1 (2025-01-20)

### What's Changed
* Patched vulnerable dependencies

### New Contributors
* @xantari made their first contribution in https://github.com/kontent-ai/delivery-sdk-net/pull/400

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/18.1.0...18.1.1

## 18.1.0 (2024-03-06)

### What's Changed
* Add support for exclude parameter by @zdenekjurka in https://github.com/kontent-ai/delivery-sdk-net/pull/391

### New Contributors
* @zdenekjurka made their first contribution in https://github.com/kontent-ai/delivery-sdk-net/pull/391

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/18.0.0...18.1.0

## 18.0.0 (2024-02-27)

### Breaking changes
* targets .NET 8.0 only
* all references to `project`, `projectId` and all related methods were renamed to `environment`, to match the current in-app state
  * `WithProjectId` → `WithEnvironmentId`
  * `DeliveryOptions.ProjectId` → `DeliveryOptions.EnvironmentId`
  * ...

### What's Changed
* Release v18 by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/390


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.9.0...18.0.0

## 17.9.0 (2023-12-12)

### What's Changed
* Vulnerabilities 11 23 by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/388
* 386 introduce workflow property to response by @Sevitas in https://github.com/kontent-ai/delivery-sdk-net/pull/387


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.8.0...17.9.0

## 17.8.0 (2023-10-30)

### What's Changed
* Issue/383 by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/385
  * updated model to match sync API response
  * added support for runtime type resolution to sync API methods

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.7.0...17.8.0

## 17.7.0 (2023-08-04)

### What's Changed
* 378 Fix the problem with IDateTimeContent BSON serialization/deserialization by @dzmitryk-kontent-ai in https://github.com/kontent-ai/delivery-sdk-net/pull/379


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.6.0...17.7.0

## 17.7.0-beta.0 (2023-05-09)  _(prerelease)_

* Release a beta with Dynamic/Universal items fetching feature implemented in #367

## 17.6.0 (2023-04-21)

### What's Changed
* Add SyncAPI support by @arguit in https://github.com/kontent-ai/delivery-sdk-net/pull/372
* Add sync api extension method by @arguit in https://github.com/kontent-ai/delivery-sdk-net/pull/374
* Update docs for extended delivery models by @Sevitas in https://github.com/kontent-ai/delivery-sdk-net/pull/375

### New Contributors
* @arguit made their first contribution in https://github.com/kontent-ai/delivery-sdk-net/pull/372

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.5.0...17.6.0

## 17.5.0 (2023-03-16)

### What's Changed
* Fix DeliveryCLientBuilder link by @Simply007 in https://github.com/kontent-ai/delivery-sdk-net/pull/366
* Make DistributedCacheManager more robust by @dzmitryk-kontent-ai in https://github.com/kontent-ai/delivery-sdk-net/pull/358


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.4.0...17.5.0

## 17.4.0 (2023-03-09)

### What's Changed
* 368 introduce IContentItem by @Sevitas in https://github.com/kontent-ai/delivery-sdk-net/pull/369


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.3.0...17.4.0

## 17.3.0 (2023-02-23)

### What's Changed
* 172 documentation by @Sevitas in https://github.com/kontent-ai/delivery-sdk-net/pull/361
* 362 Use FullName of class instead of HachCode in CacheKey by @dzmitryk-kontent-ai in https://github.com/kontent-ai/delivery-sdk-net/pull/364


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.2.0...17.3.0

## 17.2.0 (2023-01-26)

### What's Changed
* 356 Add structured DateTimeElement model by @dzmitryk-kontent-ai in https://github.com/kontent-ai/delivery-sdk-net/pull/357


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.1.0...17.2.0

## 17.2.0-beta.1 (2023-01-19)  _(prerelease)_

### What's Changed
* 356 Add structured DateTimeElement model by @dzmitryk-kontent-ai in https://github.com/kontent-ai/delivery-sdk-net/pull/357


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.1.0...17.2.0-beta.1

## 17.1.0 (2023-01-09)

### What's Changed
* upgrade github actions by @Sevitas in https://github.com/kontent-ai/delivery-sdk-net/pull/350
* Multiple delivery client factory by @Simply007 in https://github.com/kontent-ai/delivery-sdk-net/pull/347
    * **Deprecated** `AutofacServiceProviderFactory` with replacement  in form of [`MultipleDeliveryClientFactory` without dependency to Autofac](https://github.com/kontent-ai/delivery-sdk-net/blob/master/docs/configuration/multiple-delivery-clients.md)


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.0.1...17.1.0

## 17.0.1 (2022-11-10)

### What's Changed
* fix: update validation regex and related test API key by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/349


**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/17.0.0...17.0.1

## 17.0.0 (2022-08-03)

### What's Changed
* Upgraded Newtonsoft Json dependency to fix severe vulnerability issue by @MiroKentico in https://github.com/kontent-ai/delivery-sdk-net/pull/340
* **💥 Breaking change!** Migration to Kontent.ai by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/343
    * Changing package name from `Kentico.Kontent.Delivery` to `Kontent.Ai.Delivery`
    * Changing `Kentico.Kontent.*` namespaces to `Kontent.Ai.*`
* **💥 Breaking change!** Target only .NET 6 
* Documentation migration 
    * Grab commit from orphan branch by @Simply007 in https://github.com/kontent-ai/delivery-sdk-net/pull/345
    * Prepare for next beta by @Simply007 in https://github.com/kontent-ai/delivery-sdk-net/pull/344

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/delivery-sdk-net/pull/343

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/16.0.1...17.0.0

## 17.0.0-beta.2 (2022-07-29)  _(prerelease)_

### What's Changed
* Upgraded Newtonsoft Json dependency to fix severe vulnerability issue by @MiroKentico in https://github.com/kontent-ai/delivery-sdk-net/pull/340
* Migration by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/343
* Grab commit from orphan branch by @Simply007 in https://github.com/kontent-ai/delivery-sdk-net/pull/345
* Prepare for next beta by @Simply007 in https://github.com/kontent-ai/delivery-sdk-net/pull/344

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/delivery-sdk-net/pull/343

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/16.0.1...17.0.0-beta.2

## 17.0.0-beta.1 (2022-07-28)  _(prerelease)_

### What's Changed
* Upgraded Newtonsoft Json dependency to fix severe vulnerability issue by @MiroKentico in https://github.com/kontent-ai/delivery-sdk-net/pull/340
* Migration by @pokornyd in https://github.com/kontent-ai/delivery-sdk-net/pull/343

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/delivery-sdk-net/pull/343

**Full Changelog**: https://github.com/kontent-ai/delivery-sdk-net/compare/16.0.1...17.0.0-beta.1

## 16.0.1 (2022-06-16)

* `DeliveryClient` now correctly forwards Delivery API error response model instead of failing with `NullReferenceException` (#326)
* fixed potential `NullReferenceException` failure in `DeliveryEndpointUrlBuilder` + constructor `public DeliveryEndpointUrlBuilder(DeliveryOptions deliveryOptions)` was marked as obsolete (#327)

## Upgrade tips:

If you have some explicit use of constructor `public DeliveryEndpointUrlBuilder(DeliveryOptions deliveryOptions)` in your code, you should replace it with use of constructor `DeliveryEndpointUrlBuilder(IOptionsMonitor<DeliveryOptions> deliveryOptions)` instead. To adapt `DeliveryOptions` to `IOptionsMonitor<DeliveryOptions>` you can for instance use `DeliveryOptionsMonitor`.

## 16.0.0 (2022-03-17)

* Namespace unifications and project structure adjustments (#300, #303, #310)
* added [support for .NET 6 target](https://github.com/Kentico/kontent-delivery-sdk-net#kontent-delivery-net-sdk) (#301,#306)
* added support for asset renditions with configurable default preset (#308, #318)
* items enumeration can now be started from custom continuation token point (#316)
* new attribute `PropertyNameAttribute` specifies source content item element for annotated model property (might help solve #279) 

**Full Changelog**: https://github.com/Kentico/kontent-delivery-sdk-net/compare/15.0.1...16.0.0

### Upgrade tips:

Upgrade should be pretty straightforward, although you might need to update your existing `using` directives. This might be required in case of usage of any of following packages
* Kentico.Kontent.Delivery.Abstractions
* Kentico.Kontent.Delivery.Caching
* Kentico.Kontent.Delivery.Extensions.DependencyInjection
* Kentico.Kontent.Urls

## 16.0.0-beta5 (2021-12-10)  _(prerelease)_

### What's Changed
* Namespace unifications by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/300
* Test out targetting .NET 6. by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/301
* Unify namespaces in Kentico.Kontent.Delivery.Abstractions by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/303
* Added support for add asset renditions (issue 302) by @MiroKentico in https://github.com/Kentico/kontent-delivery-sdk-net/pull/308
* Set multitarget solution by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/306
* Adjust Kentico.Kontent.Urls project by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/310


**Full Changelog**: https://github.com/Kentico/kontent-delivery-sdk-net/compare/15.0.1...16.0.0-beta5

## 16.0.0-beta4 (2021-11-24)  _(prerelease)_

### What's Changed
* Namespace unifications by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/300
  * Unify namespaces in Kentico.Kontent.Delivery.Abstractions by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/303
* Targetting .NET 6. by @Simply007 in https://github.com/Kentico/kontent-delivery-sdk-net/pull/301


**Full Changelog**: https://github.com/Kentico/kontent-delivery-sdk-net/compare/15.0.1...16.0.0-beta4

## 16.0.0-beta3 (2021-11-12)  _(prerelease)_

* Targetting .NET 6 #301

## 16.0.0-beta2 (2021-11-12)  _(prerelease)_

Namespace unification - #300

## 15.0.1 (2021-11-04)

* Pagination is serializable #285 
* Release process partial fix #292

## 15.0.1-beta10 (2021-11-04)  _(prerelease)_

Test release fix #294

## 15.0.1-beta5 (2021-10-29)  _(prerelease)_

Use -p in dotnet comand instead of /p

## 15.0.1-beta4 (2021-10-29)  _(prerelease)_

Test out new version settings for dotnet pack

## 15.0.1-beta3 (2021-10-29)  _(prerelease)_

15.0.1-beta3

## 15.0.1-beta.2 (2021-10-29)  _(prerelease)_

## 15.0.1-beta1 (2021-10-21)  _(prerelease)_

#### Fixed bugs:
Fixes pagination model serialization #285

## 16.0.0-beta1 (2021-10-21)  _(prerelease)_

#### Fixed bugs:
Fixes _ImageTransformation - dependency issues_ #284

#### Breaking changes:
ImageTransformations are now in different namespace

## 15.0.0 (2021-07-22)

**New features:**
- Support for the `/languages` endpoint (#256)
- Better support for more `IDeliveryClient`s in a single project see the [docs](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Accessing-Data-From-Multiple-Projects) (#240, #254)
- Added workflow step codenames to content items (#266)
- URL generation for the Delivery endpoint was made public in `Kentico.Kontent.Urls` package

**Fixed bugs:**
- Issue in GetOriginatingAssembly function inside HttpRequestHeadersExtensions (#264)
- Error deserializing rich text content when using distributed caching (#265)
 
**Other changes:**
- SDK tracking header moved to `HttpClient.DefaultRequestHeaders` (#248)
- Bumped versions of `Microsoft.Extensions.*` dependencies to 3.1.2
- Update Benchmark.Net #276 

**Breaking changes:**
- When an item, type, or taxonomy is not found we do not throw an exception anymore, instead, `IApiResponse` has properties `IsSuccess` and `Error` that contain information about what went wrong. (#255, #251)
- Implicit operator removed from class `DeliveryItemResponse` - this syntax sugar can't be used anymore, please use the `.Item` property of the `DeliveryItemResponse`
- removed `Blocks` property from `IRichTextContent` - use the `IEnumerable` aspect of `IRichTextContent` itself (in other words, just remove `.Blocks` from your code)
- Project ID is no longer part of *EndpointUrl properties - this is an internal change that should have no impact on code used by customers (#246)
- The package `Kentico.Kontent.ImageTransformation` was renamed to `Kentico.Kontent.Urls` - there shouldn't be any breaking changes within the package
- Remove autoloading of linkeíd items #275 
  - Provide the [alternative approach](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Retrieve-modular-content-from-API-response) in case this property is necessary

## 15.0.0-rc3 (2021-06-17)  _(prerelease)_

<https://www.nuget.org/packages/Kentico.Kontent.Delivery/15.0.0-rc3>

* Remove autoloading of linked items #275 
  * Provide the [alternative approach](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Retrieve-modular-content-from-API-response) in case this property is necessary
* Update Benchmark.Net #276

## 15.0.0-rc2 (2021-02-17)  _(prerelease)_

## 14.3.0-beta1 (2021-02-17)  _(prerelease)_

## 15.0.0-rc1 (2021-02-10)  _(prerelease)_

**New features:**
- Support for the `/languages` endpoint (#256)
- Better support for more `IDeliveryClient`s in a single project see the [docs](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Accessing-Data-From-Multiple-Projects) (#240, #254)
- Added workflow step codenames to content items (#266)
- URL generation for the Delivery endpoint was made public in `Kentico.Kontent.Urls` package

**Fixed bugs:**
- Issue in GetOriginatingAssembly function inside HttpRequestHeadersExtensions (#264)
- Error deserializing rich text content when using distributed caching (#265)
 
**Other changes:**
- SDK tracking header moved to `HttpClient.DefaultRequestHeaders` (#248)
- Bumped versions of `Microsoft.Extensions.*` dependencies to 3.1.2

**Breaking changes:**
- When an item, type, or taxonomy is not found we do not throw an exception anymore, instead, `IApiResponse` has properties `IsSuccess` and `Error` that contain information about what went wrong. (#255, #251)
- Implicit operator removed from class `DeliveryItemResponse` - this syntax sugar can't be used anymore, please use the `.Item` property of the `DeliveryItemResponse`
- removed `Blocks` property from `IRichTextContent` - use the `IEnumerable` aspect of `IRichTextContent` itself (in other words, just remove `.Blocks` from your code)
- Project ID is no longer part of *EndpointUrl properties - this is an internal change that should have no impact on code used by customers (#246)
- The package `Kentico.Kontent.ImageTransformation` was renamed to `Kentico.Kontent.Urls` - there shouldn't be any breaking changes within the package

## 14.2.1 (2021-01-05)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/14.2.1

## 14.2.0 (2021-01-05)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/14.2.0

**New features:**
- #252 - Added support for the cache expiration type

## 14.1.0 (2020-12-02)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/14.1.0

**New features:**
- added support for [Collections](https://docs.kontent.ai/tutorials/manage-kontent/projects/set-up-collections) - #244

## 14.0.1 (2020-11-13)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/14.0.1

- Contains a minor fix related to [Single File Deployment on .NET 5](https://docs.microsoft.com/en-us/dotnet/core/deploying/single-file) (`Assembly.Location` can be null)

## 14.0.0 (2020-10-06)

### [New features](https://github.com/Kentico/kontent-delivery-sdk-net/milestone/16?closed=1)
- Support for distributed caching via the `IDistributedCache` interface - #196
  - there is a new implementation of `IDeliveryCacheManager` called `DistributedCacheManager` which implements the `IDistributedCache` interface using [BSON serialization](http://bsonspec.org/)
  - it's possible to register the cache using `Kentico.Kontent.Delivery.Caching.Extensions.ServiceCollectionExtensions.AddDeliveryClientCache()` by changing `DeliveryCacheOptions.CacheType` from `Memory` to `Distributed`. by default, it registers the `MemoryDistributedCache`. if you want to use a different implementation (e.g. redis, you need to register its instance before calling `AddDeliveryClientCache()`
  - **[Documentation](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Caching-responses#distributed-caching---example-from-v1400-rc1)**
- `IContentLinkUrlResolver` is now `async` (as well as several other interfaces - see the breaking changes below) - #213
- `DebuggerDisplay` attributes for models - #211
- Enabled low level access to the `ApiResponse` - #217
- Faking responses made simpler by only returning interfaces - #216 & #61
- Added support for new types of filters #229 #232

### Bugfixes
- Automatic formatting of the image transformation API - #224
- Memory leak when registering named clients - #223
- Hashcode of a cached type is now part of the cache key - #236 

### Breaking changes & upgrade advice
- all models have their interfaces extracted to `Kentico.Kontent.Delivery.Abstractions` and the SDK returns only the respective interfaces
  - for `Asset` we have `IAsset`, for `ContentType` there is an `IContentType`, etc.
- `DeliveryCacheManager` was renamed to `MemoryCacheManager`
- `IPropertyValueConverter.GetPropertyValue` was made `async` and strongly-typed. Instead of `JToken`, you receive `ContentElementValue<T>` with `Name`, `Codename`, `Type`, and `Value` properties where `Value` is of type `T` and `T` is the type of your property (`DateTime`, `string`, `int`, `Asset`....).
- methods in `IContentLinkUrlResolver`, `IModelProvider`, `IInlineContentItemsProcessor` are now `async`. their input parameters remain the same but their return type changed to `Task<T>` instead of the original `T` and they all have `Async` suffix
- some models are now more specific (contain e.g. `Guid` instead of `string` where it was appropriate) - apply `Guid.Parse()` or `Guid.ToString()` to keep your code compatible or adopt `Guid`s in your code as well
- places which returned `ContentElement` now return `IContentElement`. plus, based on the type of the element, they can return a type castable to `IMultipleChoiceElement` or `ITaxonomyElement` to allow strongly typed access to members specific to these types
- the `IDeliveryClient` now contains only `async` methods that operate upon strongly-typed models. all JSON-based methods were removed. if someone wishes to access the raw JSON, all `Delivery*Response` objects contain an object called `ApiResponse` of the `IApiResponse` type. this property contains low-level data like `string Content`, `string RequestUrl`, or `string ContinuationToken`.
  - some overloads were preserved as [extension methods](https://github.com/Kentico/kontent-delivery-sdk-net/blob/master/Kentico.Kontent.Delivery.Abstractions/DeliveryClientExtensions.cs) but are not required when implementing the `IDeliveryClient` interface
- `IInlineImage`'s properties were renamed from `AltText` and `Src` to `Description` and `Url` respectively
- All code from the `Kentico.Kontent.Delivery.ImageTransformation` namespace was extracted to a separate NuGet package `Kentico.Kontent.ImageTransformation`
- removed the - `AddDeliveryClient(this IServiceCollection services, string name, Func<IDeliveryClientBuilder, IDeliveryClient> buildDeliveryClient)` extension method - please use any other overload (they should provide enough flexibility for all scenarios)
- AngleSharp reference was upgraded to the latest stable version - 0.14.0. If you explicitly reference an older version in your projects, please follow the [migration guide](https://github.com/AngleSharp/AngleSharp/blob/master/doc/Migration.md) and upgrade to 0.14.0 too.

### Model generator
Use model generator [v6.0.0](https://github.com/Kentico/kontent-generators-net/releases/tag/6.0.0)

### NuGets
https://www.nuget.org/packages/Kentico.Kontent.Delivery/14.0.0
https://www.nuget.org/packages/Kentico.Kontent.Delivery.Rx/14.0.0
https://www.nuget.org/packages/Kentico.Kontent.Delivery.Caching/14.0.0
https://www.nuget.org/packages/Kentico.Kontent.Delivery.Abstractions/14.0.0
https://www.nuget.org/packages/Kentico.Kontent.ImageTransformation/14.0.0

## 13.0.2 (2020-08-11)

**Fixes:**
- #223 - predictable memory usage (related to `IOptionsMonitor`)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/13.0.2

## 13.0.1 (2020-03-31)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/13.0.1

**New features:**
- [support for `HttpClientFactory`](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Registering-the-DeliveryClient-to-the-IServiceCollection-in-ASP.NET-Core#httpclientfactory)
- [support for memory caching](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Caching-responses)
- [support for registering multiple clients](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Registering-the-DeliveryClient-to-the-IServiceCollection-in-ASP.NET-Core#registering-multiple-clients)
- [support for hot-reloading of configuration via `IOptionsSnapshot` and `IOptionsMonitor`](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-3.1#reload-configuration-data-with-ioptionssnapshot)
- [new best practices for working with the SDK](https://github.com/Kentico/kontent-delivery-sdk-net/wiki)
- [better support for structured rich-text rendering of assets](https://github.com/Kentico/kontent-delivery-sdk-net/issues/204)

**Breaking changes:**
- `WithHttpClient(new HttpClient())` became `WithDeliveryHttpClient(new DeliveryHttpClient(new HttpClient()))` (see the [docs](https://github.com/Kentico/kontent-delivery-sdk-net/wiki/Faking-responses))
- interfaces and models have been moved to the abstraction library `Kentico.Kontent.Delivery.Abstractions` -> add this namespace to your codefiles (or use the Code Generator v5, link below)

**Related releases:**
- [Kontent Model Generator v5](https://github.com/Kentico/kontent-generators-net/releases/tag/5.0.0)

## 12.3.0 (2019-11-14)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/12.3.0

* `GetItemsAsync` response can include the total item count matching the search criteria. Use `IncludeTotalCountParameter` to use this feature. This can be used to build paging navigation.

## 12.2.0 (2019-11-06)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/12.2.0

- Fixed some issue when used from Client-side Blazor
- Added Width and Height properties to Asset model

## 12.1.0 (2019-10-10)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/12.1.0

- Items feed exposes the current continuation token. Use the `ContinuationToken` property of the `DeliveryItemsFeedResponse`.
- Items feed is easier to mock and test. Use the `IDeliveryItemsFeed` interface to create your own.

## 12.0.0 (2019-10-08)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/12.0.0

**New features**

- Response models provide information whether content is stale. Use the `HasStaleContent` property to determine content status.
- Items can be enumerated in a streaming fashion. Use the `GetItemsFeed` method to create a feed. Use the `HasMoreResults` property and the `FetchNextBatchAsync` method to enumerate the feed.

**Breaking changes**

- `GetTaxonomyAsync`, `GetTypeAsync` and `GetContentElementAsync` methods return response models instead of taxonomy group, content type or content type element models. Use `Taxonomy`, `Type` or `Element` properties to get response content. There is also an implicit conversion from response models to their content.
- Improved retry policy. Apart from error responses it also handles connection issues and it no longer depends on `Polly`. Use the `WithDefaultRetryPolicyOptions` method to customize settings of the default retry policy. Create a custom retry policy by implementing `IRetryPolicy` and `IRetryPolicyProvider` interfaces.
- Updated client options.
  - Use the `UseProductionApi` method instead of the `UseProductionApi` property.
  - Use the `WaitForLoadingNewContent` method instead of the `WaitForLoadingNewContent` property.
  - Use the `UseProductionApi` method with a secure access key parameter instead of the `UseSecuredProductionApi` method when secure access to the Delivery API is enabled.
  - Use `UseSecureAccess` and `SecureAccessApiKey` properties instead of `UseSecuredProductionApi` and `SecuredProductionApiKey` properties to configure client when secure access is enabled.

## 11.0.3 (2019-09-24)

https://www.nuget.org/packages/Kentico.Kontent.Delivery/11.0.3

## 11.0.2 (2019-09-24)

Update Nuget icon.

## 11.0.1 (2019-09-24)

**Breaking changes**

- Kentico Cloud is now **Kentico Kontent**.
As we finalize our move to Content as a Service, we decided to reflect that in our brand so we renamed repository, Nuget feed, namespaces, and all related code.

## 11.0.0 (2019-09-24)

**Breaking changes**

- Kentico Cloud is now Kentico Kontent.
As we finalize our move to Content as a Service, we decided to reflect that in our brand so we renamed repository, Nuget feed, namespaces, and all related code.

## 11.0.0-beta1 (2019-09-11)  _(prerelease)_

**New features**

- Response models provide information whether content is stale.

**Breaking changes**

- `GetTaxonomyAsync`, `GetTypeAsync` and `GetContentElementAsync` methods return response models instead of taxonomy group, content type or content type element models. Use `Taxonomy`, `Type` or `Element` properties to get response content. There is also an implicit conversion from response models to their content.

## 10.0.1 (2019-04-23)

https://www.nuget.org/packages/KenticoCloud.Delivery/10.0.1

**Fixes:**
- https://github.com/Kentico/delivery-sdk-net/issues/160 - GetExecutingAssembly() is not accesible in Xamarin.

## 10.0.0 (2019-03-11)

https://www.nuget.org/packages/KenticoCloud.Delivery/10.0.0

**Fixes:**
- #152 - Code First approach
- #153 - Inline content resolver in v9 has unnecessary wrapping

**Breaking changes:**
- `ResolvedContentItemData` class was removed as it served just as an unnecessary wrapper class
* Every "code first" occurence in the code was removed i.e.:
   * `ICodeFirstModelProvider`, `ICodeFirstTypeProvider` and `ICodeFirstPropertyMapper` interfaces where  renamed to `IModelProvider`, `ITypeProvider` and `IPropertyMapper`
   * Methods for registration in `DeliveryClientBuilder` - `WithCodeFirstModelProvider`, `WithCodeFirstTypeProvider` and `WithCodeFirstPropertyMapper` were renamed to `WithModelProvider`, `WithTypeProvider` and  `WithPropertyMapper`
   * `CodeFirstResolvingContext` class was renamed to `ResolvingContext`

## 9.0.1 (2018-12-17)

https://www.nuget.org/packages/KenticoCloud.Delivery/9.0.1

**Fixes:**
- #145 - Missing content model causes DeliveryClient to silently fail
- #146 - Resolving inline items with IInlineContentItemsResolver is broken
- #149 - Custom default inline content items resolver registered through builder is not used

**New features:**
- some interfaces were removed or made simpler

**Breaking changes:**
* Non-generic `IInlineContentItemsResolver` was removed as it was an unusable interface that might eventually cause confusion in combination with `ITypelessInlineContentItemsResolver`. 
* removed members from `IInlineContentItemsProcessor`

**Non-breaking changes:**
* An independent `ITypelessInlineContentItemsResolver` interface was introduced to wrap and hide `IInlineContentItemsResolver<TContentItem>` resolvers and provide them in a non-generic way to the `InlineContentItemsProcessor` that later puts inline content items through the resolver in order to obtain their string representation.
* New extension methods for IServiceCollection were introduced to allow registration of custom inline content items resolvers

## 8.0.0 (2018-11-12)

**New features:**
- Better [dependency injection support](https://github.com/Kentico/delivery-sdk-net/wiki/Using-the-ASP.NET-Core-Configuration-API-and-DI-to-Instantiate-the-DeliveryClient)

**Breaking changes:** 

- Removed .NET Framework 4.5 as a target - now the SDK targets .NET Standard 2.0 which is supported on .NET Framework 4.6.1 and higher + .NET Core 2.0 and higher
- class `DeliveryClient` is no longer public - requests are now made through instance implementing `IDeliveryClient` which can be created using new `DeliveryClientBuilder` class
- Added a builder class for `DeliveryOptions`
- Added an extension method on `IServiceCollection` that registers `IDeliveryClient` implementation
- Custom implementation of resolvers, processors, mappers can no longer be set to public properties - now they can be set through `DeliveryClientBuilder` class or by registering them to the `ServiceCollection`
- `ConfigurationManagerProvider` class has been removed so the `GetDeliveryOptions` method for retrieving `DeliveryOptions` from web.config is no longer available.
- Exception is [not thrown](https://github.com/Kentico/delivery-sdk-net/issues/126) when strong type doesn't exist during deserialization. Instead, null is returned for that object.

**NuGet:**
- [8.0.0](https://www.nuget.org/packages/KenticoCloud.Delivery/8.0.0)

## 7.0.0 (2018-10-18)

**Breaking changes:**
- Modular content renamed to linked items #130 

**NuGet:**
 - https://www.nuget.org/packages/KenticoCloud.Delivery/7.0.0

## 6.0.0 (2018-09-13)

- #93 - Implemented SDK tracking header for measurement popularity
- #101 - Breaking change - A `GetCodename` method signature is introduced to the `ICodeFirstTypeProvider` interface.
  - Use [Kentico Cloud Model Generator 1.5.198](https://github.com/Kentico/cloud-generators-net/releases/tag/v1.5.198) or newer to regenerate your models
- #108 - Enable [source link](https://github.com/dotnet/sourcelink) for debugging

## 5.0.0 (2018-08-06)

- #100 - Implemented a retry policy 
- #110 - Added netstandard2.0 as a target 
  **Breaking change**: Upgraded from netstandard1.3 to netstandard2.0

## 4.14.0 (2018-06-20)

https://www.nuget.org/packages/KenticoCloud.Delivery/4.14.0

## New features and improvements
- Added support for Image transformation

## 4.13.0 (2018-02-08)

**Changes:**
- Added type filters #86 
- Support for secured production Delivery API #96

## 4.12 (2017-11-01)

https://www.nuget.org/packages/KenticoCloud.Delivery/4.12.0

## Fixed bugs
- Long queries were producing bad requests

## 4.11 (2017-10-25)

https://www.nuget.org/packages/KenticoCloud.Delivery/4.11.0

## New features and improvements
- Added type filters
- Long query string handling
- Fixed support of null values in "modular_content" within ritch text.

## 4.9 (2017-09-07)

https://www.nuget.org/packages/KenticoCloud.Delivery/4.9.0

## New features and improvements
- Added support for getting Taxonomy groups

## 4.8 (2017-08-29)

https://www.nuget.org/packages/KenticoCloud.Delivery/4.8.0

## New features and improvements
- Added WaitForLoadingNewContent option - Allows to wait for updated content. It should be used when you are acting upon a webhook call.

## 4.7 (2017-08-13)

https://www.nuget.org/packages/KenticoCloud.Delivery/4.7.0

## New features and improvements

- [Support for custom types in models via "Value Converters"](https://github.com/Kentico/delivery-sdk-net/wiki/Support-for-custom-types-in-models-via-%22Value-Converters%22)
- fully refactored configuration management
  - added [.NET Core configuration support](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration) ([IOptions<DeliveryOptions>](https://github.com/Kentico/delivery-sdk-net/wiki/Using-the-ASP.NET-Core-Configuration-API-and-DI-to-Instantiate-the-DeliveryClient))
  - added a configuration [provider](https://github.com/Kentico/delivery-sdk-net/wiki/Loading-DeliveryClient-settings-from-web.config) for legacy web.config appSettings approach
  - added full [support](https://github.com/Kentico/delivery-sdk-net/wiki/Using-the-ASP.NET-Core-Configuration-API-and-DI-to-Instantiate-the-DeliveryClient) for instantiation via constructor [DI](https://en.wikipedia.org/wiki/Dependency_injection)
- all response objects now contain [`ApiUrl` for easier debugging](https://github.com/Kentico/delivery-sdk-net/blob/04b6b6694e4ae43c359e797cf570d88c87e167d5/KenticoCloud.Delivery/Responses/AbstractResponse.cs#L14)
- better [support for unit testing](https://github.com/Kentico/delivery-sdk-net/wiki/Faking-responses) (you can now fake HttpClient responses)
- added more unit tests!

## Fixed bugs
- [Asset description was not always initialized](https://github.com/Kentico/delivery-sdk-net/issues/68)

## Closed pull reuqests
See all closed pull reuqests in the latest [milestone](https://github.com/Kentico/delivery-sdk-net/milestone/3?closed=1).

## Special thanks
- [Jarosław Jarnot](https://github.com/jjarnot-vimanet) ([Vimanet](https://vimanet.com/)) for refactoring of the Configuration management
- [Lee Conlin](https://github.com/hades200082) for [plenty](https://forums.kenticocloud.com/discussion/comment/159#Comment_159) of valuable [feedback](https://forums.kenticocloud.com/discussion/comment/157#Comment_157)
- [Jacob Mojiwat](https://forums.kenticocloud.com/profile/JacobMojiwat) for [valuable feedback](https://forums.kenticocloud.com/discussion/49/support-nodatime-in-property-types#latest)

## 4.5 (2017-06-01)

### NuGet
https://www.nuget.org/packages/KenticoCloud.Delivery/4.5.0

### New features and improvements
- Support of Modular Content in Rich Text Elements
- Project migrated to Visual Studio 2017
- Several little tweaks to make the SDK more robust and the code less verbose
- Improved XML documentation
