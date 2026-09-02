# Shared plumbing across the three SDKs — second extraction pass

What is still duplicated between `src/delivery`, `src/sync` and `src/management` after the first round
moved retry predicates, tracking headers, Refit response reading, named-client resolution, the options
accessor and the options copier into `src/common`. Written 2026-09-01 against `vnext` at `ace93792d`.

The measure throughout is the one `src/common/README.md` sets: shared source is for what is
*identical*, not for what could be unified with enough parameters. Every candidate below was checked
by reading the three copies side by side; "identical modulo the options type" means a normalised diff
of the bodies is empty.

**Verdict: seven extractions and one deletion are worth making, none of them large. Together they
remove roughly 300 lines of triplicated plumbing, and three of them close real gaps rather than just
saving lines: Sync and Management each define their transport twice where Delivery defines it once,
the shared predicates are tested unevenly per product, and the HTTP timeout rule is stated in three
places with the same three-line comment. What should not be
extracted is listed too, with the reason, so it is not re-litigated.**

---

## 1. Where the plumbing stands

| Concern | Delivery | Sync | Management | State |
|---|---|---|---|---|
| Options registration + validation | inline ×2 | `RegisterOptions` | `RegisterOptions` | identical helper in two, inline in one |
| Resilience handler gate | `ConfigureResilienceHandler` | same | same | identical modulo options type |
| Default resilience pipeline | retry + 30 s timeout | same | idempotency-aware, no timeout | two identical, one deliberately different |
| `HttpClient.Timeout` rule | DI | DI + factory | always `options.Timeout` | same expression and comment in three places |
| Keyed Refit registration | `AddKeyedTransient` | same | same, twice per name | identical modulo API type |
| Standalone handler chain | none (private container) | `SyncApiFactory` | `ManagementApiFactory` | identical chain, two files |
| Standalone pipeline build | none | `BuildResiliencePipeline` | `BuildResiliencePipeline` | identical |
| Source header resolution | `HttpRequestHeadersExtensions` | inline in `TrackingHandler` | `HttpRequestHeadersExtensions` | identical modulo attribute type |
| Host trust and rewrite | `DeliveryAuthenticationHandler` | `SyncAuthenticationHandler` | not applicable | identical pure-`Uri` logic, same two host names |
| Error envelope assembly | `BuildErrorAsync` | same | same | identical modulo `Error` type |
| Client factory | 3 methods | 2 | 2 | already one line each via `KeyedClients` |
| Options GUID validation | `Validate` | `Validate` | `Validate` ×2 | same checks, product-specific messages |

## 2. Extract — `src/common`

Ordered by what each one buys beyond line count.

### 2.1 `OptionsRegistration.RegisterValidated<TOptions>` — closes a gap

> **Done on `unify-registration`**, as `src/common/OptionsRegistration.cs`; all three products call it.

`RegisterOptions` now exists, identically, in Sync and Management (the Management copy landed in
`6aae6e94c`), and Delivery still writes the same block inline twice. One generic:

```csharp
internal static void RegisterValidated<TOptions>(
    IServiceCollection services, string name, Action<OptionsBuilder<TOptions>> configure)
    where TOptions : class
```

Same body as the two helpers: named registration, unnamed as well for the default name, validate
data annotations, validate on start. `src/common` already references `Microsoft.Extensions.Options`
through `OptionsAccessor.cs` and DI through `KeyedClients.cs`, so no new dependency. Removes two
helpers and Delivery's inline pair; every product's default-name behaviour then comes from one place.

### 2.2 `HttpClientTimeouts.Resolve` — the rule is stated three times

> **Done on `unify-registration`**, as `src/common/Http/HttpClientTimeouts.cs`, pinned by a shared test. Two call sites remain after 2.6, Delivery and Sync.

Delivery's DI path, Sync's DI path and Sync's standalone factory each carry this expression and the
same three-line comment above it:

```csharp
httpClient.Timeout = options.Timeout
    ?? (options.EnableResilience && configureResilience is null
        ? Timeout.InfiniteTimeSpan
        : httpClient.Timeout);
```

A pure function, `Resolve(TimeSpan? configured, bool defaultPipelineBoundsAttempts, TimeSpan fallback)`,
with the comment on it once. Management does not use it — its ceiling is always `options.Timeout`,
which is the documented divergence — and stays as it is. Small, but it is the kind of rule that drifts
silently between a DI and a standalone path, and the test that pins it can then be written once.

### 2.3 `ResilienceHandlers.AddOptionsGated<TOptions>` — identical in all three

> **Done on `unify-registration`**, as `src/common/Http/ResilienceHandlers.cs`; Management passes its own default pipeline.

`ConfigureResilienceHandler` reads the named options off the monitor, returns early when resilience is
disabled, and otherwise runs the caller's hook or the default. Three copies, identical modulo the
options type and the default delegate:

```csharp
internal static void AddOptionsGated<TOptions>(
    IHttpClientBuilder builder, string handlerName, string clientName,
    Func<TOptions, bool> isEnabled,
    Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configure,
    Action<ResiliencePipelineBuilder<HttpResponseMessage>> configureDefault)
```

Management keeps passing its own `ConfigureDefaultResilience`; the gate is the same for a write API.

### 2.4 `DefaultResilience.ConfigureReadPipeline` — Delivery and Sync are the same file

> **Done on `unify-registration`**, as `src/common/Http/DefaultResilience.cs`, with Sync's three composed-pipeline tests moved to a shared test source that Delivery now runs too.

Both build retry with the same options, the shared predicates, the shared `Retry-After` delay
generator, then a 30-second per-attempt timeout. Byte-identical apart from a comment. One shared
method; Management's idempotency-aware pipeline stays in Management, which is README rule 4 applied
exactly: the divergence is visible because the common file has no retry-by-method logic in it.

### 2.5 `RefitClients.AddKeyed<TApi>` — one shape, four registrations

> **Resolved by 2.9.** The hand-rolled registration no longer exists; Refit's keyed generated
> registration replaced it in all three products.

```csharp
services.AddKeyedTransient(name, (sp, _) =>
    RestService.For<TApi>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName), settings));
```

Delivery, Sync, and Management twice. Trivial on its own; worth folding into the same file as 2.3
so the "named HTTP client → keyed Refit client" step reads as one sentence in each product.

### 2.6 One transport per product — give Sync and Management Delivery's private container

> **Planned separately.** This is a larger, surgical change with its own invariants and abandon
> criteria; the step-by-step plan is `docs/standalone-client-container-plan.md`. It goes first in
> the sequencing below, because once it lands the extractions in 2.1–2.5 cover the whole transport.

`SyncApiFactory.CreateHttpClient` and `ManagementApiFactory.CreateHttpClient` assemble the same chain
in the same order — primary, auth, tracking, optional `ResilienceHandler` — and
`SyncClient.BuildResiliencePipeline` and `ManagementClient.BuildResiliencePipeline` are identical.
Extracting a shared chain builder was the first thought; it is the wrong cut, because those files
exist only so that a container-free client can build what the DI path already builds.

Delivery does not have that duplication. Its builder spins up a private `ServiceCollection`, calls
`AddDeliveryClient` on it, builds a provider with `ValidateOnBuild`, and hands the provider to the
client as the resource it owns. The transport is defined once, in the DI registration. Sync and
Management can do the same, and then the second definition — factory, pipeline builder, Management's
`CompositeDisposable`, Sync's owned `HttpClient` — is deleted rather than shared (the composite was briefly shared in `src/common`, then deleted on `client-builders` once all three standalone paths owned only the provider).

Only this direction is open. Delivery's builder exposes `ConfigureServices`, and the Caching package
registers `WithMemoryCache` / `WithHybridCache` through it; a hand-built chain has no container for
those to register into, so Delivery cannot move the other way without breaking a shipped extension
point. Sync and Management gain a container the caller never sees.

What it settles:

- **The `HttpClientHandler` note both reviews left open.** A container-built client goes through
  `IHttpClientFactory`, so `HttpClientDefaults.ConfigureConnectionRecycling` applies to standalone
  clients as well. The question stops existing.
- **`SnapshotOptionsAccessor`** may lose its last caller; every path then reads the monitor.

What it costs:

- **The exception `Build()` throws.** Sync and Management document `ValidationException`; Delivery
  documents `OptionsValidationException` because its validation runs inside the container. Choose one
  for all three. An explicit `Validator.ValidateObject` before the container is built keeps
  `ValidationException` everywhere, and Delivery would gain the plainer type. Changelog entry either way.
- **The test seam.** Management's `MockClientFactory` and Sync's `primaryHandler` constructor
  parameter inject the mock into the hand-built chain. With a container, the mock goes in through
  `configureHttpClient` and `ConfigurePrimaryHttpMessageHandler`, the seam the DI tests already use.
  Three helper files change; the tests do not.
- **The public `ManagementClient(ManagementOptions)` constructor** would build a container inside a
  constructor. It can stay and route through the builder's code, documented as its shorthand.
- **Docs.** The Management builder's "does not spin up a private service provider" and the Sync
  README's "no container involved" were both written as a stance, not after a failure; both flip to
  Delivery's wording.
- **A provider per built client.** Negligible for a documented application-lifetime singleton.

### 2.7 `SourceTrackingHeader<TAttribute>` — the third copy of "must never throw"

Delivery's and Management's `HttpRequestHeadersExtensions` and Sync's `TrackingHandler` each hold
the same ~35 lines: a `Lazy` over the SDK header, a `Lazy` over the source header, a resolver that
walks the stack for the originating assembly, reads the product's attribute, composes the value
through `SdkTrackingHeaders`, and a blanket catch with the same comment explaining why it must never
throw. The only difference is the attribute type, and the three attributes have the same six members.

`src/common/README.md` says attribute reading stays with each SDK because the attribute is public
surface. That constraint is about the *type*, and it holds: the attributes stay where they are. What
can move is the reading, keyed on an internal interface the three public attributes implement:

```csharp
internal interface ISourceTrackingHeader
{
    string? PackageName { get; }
    int MajorVersion { get; }
    int MinorVersion { get; }
    int PatchVersion { get; }
    string? PreReleaseLabel { get; }
    bool LoadFromAssembly { get; }
}

internal static class SourceTrackingHeader<TAttribute>
    where TAttribute : Attribute, ISourceTrackingHeader
{
    internal static string? Value { get; }  // Lazy, resolved once per process, never throws
}
```

An internal interface on a public sealed class adds nothing to the public surface, and the approval
printer does not emit interfaces that are not themselves public. Each product's tracking handler
becomes three lines. Verify the printer claim against a `.received.txt` before relying on it.

### 2.8 `RequestHosts` — two copies of the same host list

`DeliveryAuthenticationHandler.ShouldRewriteUri` and `SyncAuthenticationHandler.IsTrustedHost` are
the same predicate: the configured host, or one of the two `*.kontent.ai` delivery hosts. The rewrite
that follows — scheme, host and port from the configured base, path and query kept — is the same
`UriBuilder` block. Both are pure `Uri` functions that touch no product type:

```csharp
internal static bool IsTrusted(Uri request, Uri configuredBase);
internal static Uri WithHost(Uri request, Uri configuredBase);
```

Management sends everything to one host and does not rewrite, so it does not take the file. Two
copies of the host allow-list is the concrete risk here: a third first-party host would be added to
one and missed in the other.

### 2.9 Use Refit's own keyed registration instead of hand-rolling it — the strongest alignment available

> **Done on `unify-registration`, after moving to Refit 15.2.0.** On 14.0.1 this was blocked: Refit 14's
> keyed registration is the *reflection* one (`RequestBuilder.ForType<T>` throws `NotSupportedException`
> without `Refit.Reflection`, which no product references), and its generated registration had no keyed
> variant. Refit 15.0.0 added `AddKeyedRefitGeneratedClient<T>(serviceKey, settings, httpClientName)`, which
> is the exact shape below. All three products now register through it; the hand-rolled
> `AddKeyedTransient` is gone. The primary-handler test in each DI suite pins that the SDK's connection
> recycling still wins over the plain `HttpClientHandler` Refit installs.

Two facts checked against the binary that make it viable:

- **The HTTP client name is a parameter**, so the products keep `Kontent.Ai.Sync.HttpClient.{name}`
  and its siblings. The duplicate-registration error in `KeyedClients.EnsureNotRegistered` quotes that
  name, and the three `ServiceCollectionExtensionsTests` files observe it; neither has to change.
- **The builder path still owns its transport.** After the container plan, `Build()` draws the
  `HttpClient` from the factory by that same name and calls `RestService.For` on it. It never resolves
  the keyed Refit service, so it does not care who registered it. The small `Create…Api(HttpClient)`
  helper that plan introduces then has one caller, the builder, and stays for that.

What changes per product is one lambda replaced by one Refit call. What is gained is that the
registration a consumer reads in the source is the one Refit documents, and that any future Refit
behaviour attached to its registration — settings validation, its own handler ordering guarantees —
arrives without the SDK re-implementing it.

Two things to confirm while doing it, neither expected to bite: that Refit registers the interface
with the transient lifetime the products rely on today (the keyed singleton client resolves it once,
so a different lifetime would be harmless but worth knowing), and that Refit's registration adds no
handler of its own ahead of the products' chain, since the resilience-outside-auth order is what the
retry tests pin. Both are a single test run to settle.

**Order:** done, in the same PR as the container plan.

## 3. Extract — `src/testing`

### 3.1 Shared tests for the shared files

> **Done on `unify-registration`.** `src/testing/Http/` holds the three files below; each product
> includes the ones for the common files it compiles. The product files kept only their composed
> behaviour, and Delivery's separate predicate file went away entirely.

The predicates, the delay generator and the header composition are tested in Sync (`225` lines) and
Management (`374`, including its own idempotency cases) with near-identical unit tests, and in
Delivery with `27` lines. The `X-KC-SOURCE` composition has no test in Sync. So the code that is
guaranteed identical across the three assemblies is *verified* to different depths in each.

`src/testing`'s charter is exactly this — "one behaviour, everywhere" — and its mechanism already
works for the approval printer. A test source per common file, compiled into all three test
projects, runs the same assertions against each assembly's copy:

- `HttpRetryPredicatesTests.cs` — the status-code table and the eight exception cases
- `HttpRetryDelayTests.cs` — the three `Retry-After` cases
- `SdkTrackingHeadersTests.cs` — version stripping, pre-release formatting, package-name fallback

Product-specific composition tests (Management's idempotency matrix, Sync's through-the-handler retry
tests) stay in the product. Expect the three product files to shrink by half and Delivery's
coverage of the shared code to become equal to the others'.

## 4. Do not extract

- **`BuildErrorAsync`.** The five-line switch over `ParsedError<TError>` is the same in all three,
  but the arms use `with` on the product's `Error` record. A shared version needs three delegates
  (attach exception, from message, empty) and reads worse than the switch it replaces.
- **The tracking `DelegatingHandler` itself.** After 2.7 it is ten lines, and two of them log through
  the product's `LoggerMessages`, which is a product type. Not worth a fourth parameter.
- **Client factories.** Two methods each, one line each, already through `KeyedClients`. Delivery's
  extra `TryGet` is a real surface difference.
- **Options GUID validation.** The checks match but the messages are product voice — Management's
  "haven't you accidentally passed the API key" hint is worth keeping. A shared checker returning a
  `ValidationResult?` would have to take the message as a parameter, at which point it saves nothing.
- **The idempotent dispose pattern.** `Interlocked.Exchange` and a null-conditional dispose. Three
  copies of four lines is cheaper than a base class or a helper nobody would look for.
- **Anything that would put a product type in `src/common`.** The auth handlers read `GetBaseUrl()`
  and `GetApiKey()` off product options; they stay, and take 2.8's pure functions as helpers.

## 5. Delivery-internal: the query builders

Not a `src/common` matter, but the same question — where is the repeated code and what is the
honest cut — and the earlier plan (`docs/delivery-query-builders-plan.md`, §5) left it undone.
Re-measured on `vnext`:

| Repeated piece | Files | Occurrences |
|---|---|---|
| `LogQueryFailed` immediately followed by `LogQueryCompleted` | 10 | 16 |
| `EnsureApiResult` → failure branch → `cached?.Value ?? apiResult.Value` merge | 6 | 6 |
| `CachedQuerySource.FailSafeHit` ternary over `FailSafeHit`/`CacheHit` | 6 | 6 |
| `bool? waitForLoadingNewContent = _waitForLoadingNewContent ? true : null` | 10 | 10 |
| `CreateNextPageFetcher` + `CreateNextPageQuery` snapshot pair | 7 | 14 |

Two cuts, in this order:

**5.1 A `LogOutcome` method on `QueryLoggingHelper` — do first, no design needed.** Every failure
path writes `LogQueryFailed` then `LogQueryCompleted` with the same arguments, and every success path
writes the second alone. One method taking the stopwatch and the `IDeliveryResult` replaces sixteen
two-line pairs and their surrounding `if`. It is a method on an existing helper, not a new
abstraction, and it removes the most frequent repetition in the folder.

**5.2 An extension method on `CachedQueryOutcome<TCached, TApi>` — the plan's §5(A), confirmed.**
The block after `CachedQueryExecutor.ExecuteAsync` is the subtle one and appears six times: classify
the source, log a hit, `EnsureApiResult`, log and convert a failure, merge `cached?.Value` with the
API value and `cached?.DependencyKeys` with freshly built ones. As an extension method it takes what
varies — the logger, the query name and target for `EnsureApiResult`, a `select` from the API type
to the cached type, and the dependency builder — and returns the plan's `ResolvedQuery` with a
`Kind`. Each call site keeps a four-arm switch that names its exits. Two type parameters, two
delegates; the storage-mode split in `ItemQuery`/`ItemsQuery` stays inside their `runCachedFetch`
lambda, untouched. The plan's projected saving of about 130 lines across six files stands.

**Not this:** a generic base with the entity, API, cached and public types as parameters and the
fluent setters, fetch, dependency builder and next-page logic as constructor delegates. The plan
rejected it and the reason has not changed: nine arguments to save shell code that is verifiable by
eye. `TypesQuery` and `TaxonomiesQuery` being 97% the same file is boring duplication, and the tests
catch drift in it.

## 6. Sequencing

1. **2.6** first, by its own plan — the container move for Sync and Management deletes their
   standalone factories and pipeline builders outright, so everything after it has one composition
   site per product to work on.
2. **3.1** — done.
3. **2.1, 2.2, 2.3, 2.4** — done, in one commit. 2.5 and 2.9 are done too.
4. **The registration surface itself** — done on `client-builders`, by
   `docs/client-registration-builder-plan.md`: one builder per client, implemented once in
   `src/common/Clients`, which is where 2.1 and 2.3 ended up living.
4. **2.7** and **2.8** — each self-contained, each with the approval snapshot as the check that no
   public surface moved.
5. **5.1** then **5.2** — Delivery only, `TypeQuery`/`TaxonomyQuery` first as the plan sequences it.

Every step is internal. The approval snapshots must come back byte-identical for each, which is
the same check the first extraction round used.
