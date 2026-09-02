# One transport per product: the standalone Sync and Management clients

Plan for removing the hand-built HTTP transport under `SyncClientBuilder` and `ManagementClientBuilder`
and having both build through the same registration the DI path uses, the way `DeliveryClientBuilder`
already does. Written 2026-09-01 against `vnext` at `ace93792d`. Companion to
`docs/sdk-plumbing-extraction-analysis.md`, §2.6, which explains why this direction and not the other.

> [!NOTE]
> **Status: implemented on `unify-registration`, in the four commits §10 lays out.** Every claim below
> about the pre-change behaviour was read off the code or a test on the commit named above. Each step
> left the tree green and both public API approval snapshots byte-identical; none of the abandon
> criteria in §9 was met. The text below is the plan as written, kept as the record of why.

**Verdict: doable without a public API change and without a regression in anything a test currently
pins. There is exactly one behavioural delta, and it is a timing delta rather than a semantic one:
after a standalone client is disposed, requests through it fail immediately as today, but the pooled
connections close when the factory-owned handler is collected rather than at the moment of disposal.
That is the same behaviour every DI-resolved client already has. If that delta is not acceptable, the
plan is abandoned in §9, not adapted.**

---

## 1. What changes and what must not

### 1.1 The change

Today `SyncClient` and `ManagementClient` each have a second definition of their transport, used only
by the builder and the public `ManagementClient(ManagementOptions)` constructor:

| Product | Hand-built pieces | Lines |
|---|---|---|
| Sync | `SyncApiFactory.CreateHttpClient`, `SyncClient.BuildResiliencePipeline`, the four-parameter internal constructor | ~100 |
| Management | `ManagementApiFactory` (three methods), `ManagementClient.BuildDependencies`, `BuildResiliencePipeline`, `CompositeDisposable` | ~130 |

After the change the builder does what `DeliveryClientBuilder.Build` does: a private
`ServiceCollection`, the product's own `Add…Client` registration, a provider built with
`ValidateOnBuild`, and a client constructed through the same internal factory the container uses,
handed the provider as the resource it owns. The hand-built pieces are deleted, not shared.

### 1.2 Invariants — each has a test that must stay green unchanged

| Invariant | Pinned by | Preserved how |
|---|---|---|
| `Build()` and the constructor throw `ValidationException` on invalid options | `SyncClientBuilderTests.Build_InvalidOptions_ThrowsValidationException`, `ManagementClientBuilderTests.Build_InvalidOptions_ThrowsValidationException`, `StandaloneClientTests.InvalidOptions_AreRejectedAtConstruction` | The explicit `Validator.ValidateObject` call stays and runs *before* the container is built. The options pipeline's own validation then never fires first. |
| Requests through a disposed standalone client fail with `ObjectDisposedException` in the result | `StandaloneClientTests.DisposingTheClient_ReleasesItsHttpClient` | The client keeps owning the `HttpClient` it draws from the factory and disposes it first. `HttpClient.SendAsync` throws `ObjectDisposedException` after `Dispose` regardless of who owns the handler. |
| Dispose and DisposeAsync are idempotent, on both paths, and a no-op for DI-resolved clients | `StandaloneClientTests.Dispose_IsIdempotent`, `DisposeAsync_IsIdempotent`, `DisposeTests.*` | Unchanged `Interlocked.Exchange` guard; `ownedResources` is `null` on the DI path exactly as now. |
| The standalone chain equals the container's chain | `StandaloneClientTests.StandaloneClient_UsesTheSameHandlerChainAsTheContainer` | Becomes true by construction. The test stays as the proof. |
| Default resilience retries, disabled resilience does not, a custom pipeline replaces the default | `StandaloneClientTests.StandaloneClient_RetriesTransientFailures`, `…_WithResilienceDisabled_DoesNotRetry`, `…_CustomResilience_ReplacesTheDefault`, `ManagementClientBuilderTests.WithResilience_IsChainable` | The builder forwards `configureResilience` into `Add…Client`, whose gate is already tested. |
| The whole-call timeout rule | `ServiceCollectionExtensionsTests` in both products (four cases each) | The builder path now *is* that code. The four `CreateHttpClient_*` tests in Sync that pinned the factory's copy are deleted with the factory; their assertions are the DI tests. |
| Preview mode targets the preview endpoint | `StandaloneClientTests.PreviewMode_TargetsThePreviewEndpoint` | Base address comes from the registration's `ConfigureHttpClient`, already tested on the DI path. |
| Public surface | Approval snapshots in both products | `SyncClient`, `SyncClientBuilder`, `ManagementClient` including its `.ctor(ManagementOptions)`, and `ManagementClientBuilder` keep every public member. Only internal constructors and internal types move. |

### 1.3 The two deltas, stated once

A standalone client today disposes an `HttpClient` that owns its handler chain, so `Dispose` closes
the pooled connections synchronously. A factory-created `HttpClient` is constructed with
`disposeHandler: false`; the primary handler belongs to `IHttpClientFactory`, which disposes it after
the handler lifetime expires and the tracking entry is collected. Requests stop at once — the
`HttpClient` is disposed — but sockets close a little later, and process exit closes them anyway.
This is the DI path's behaviour today in all three SDKs and Delivery's standalone behaviour too.
It goes in both changelogs, worded as above.

The second delta was found in review, after the change landed. The hand-built path held the
caller's options object - Management's `SnapshotOptionsAccessor` and Sync's constructor took the
instance itself - so a property changed on it after `Build()` was read on the next request. The
container path copies the instance, as `Add…Client(options)` always has, and reads its own copy
through `IOptionsMonitor`. A key rotated on the caller's object after `Build()` therefore no longer
reaches the client. Nothing documented the aliasing and Delivery's standalone client never had it;
it is recorded in both changelogs as the behaviour change it is.

### 1.4 History — this question has been decided before, in both directions on the same day

Sync had a private container until `18ba7c5c0` (2026-08-05, "build the standalone sync client
without a service container"), which the 2.0.0-rc.1 changelog records with two reasons: the design
needed `OwnedSyncClient`, a wrapper whose only job was to forward every method and dispose the
provider; and `ConfigureServices` existed only to reach into that container, with replacing the
resilience pipeline the one thing it was used for, so it became `WithResilience`. Sync then matched
`ManagementClientBuilder`, which had hand-built its chain since `0f3a91701`. The disposal test in
§1.2 was written in that commit to pin the property that motivated it.

The same day, `a71b49198` and `5766a5f96` gave Delivery the opposite design: the client owns the
provider directly through `ownedResources`, with no wrapper. That dissolves Sync's first reason, and
this plan keeps the container private with no public hook, so the second does not return. The plan
therefore proposes Delivery's design, not the one Sync removed; the earlier decision was sound
against the wrapper it was looking at.

## 2. Verified facts the plan relies on

- **The concrete container is already a transitive dependency of both packages**, at the same
  version the repo pins (`Microsoft.Extensions.DependencyInjection` 10.0.10, via
  `Microsoft.Extensions.Http` → `Microsoft.Extensions.Logging`). Adding a direct `PackageReference`
  changes nothing a consumer restores. `Directory.Packages.props` already carries the version.
- **`configureHttpClient` runs last in every registration**, so `ConfigurePrimaryHttpMessageHandler`
  from a test overrides the `SocketsHttpHandler` that `HttpClientDefaults.ConfigureConnectionRecycling`
  installs. This is the seam the tests move to, and the ordering comment in each registration already
  promises it.
- **`ValidateOnBuild` does not invoke factory delegates**, so a Management container whose
  `SubscriptionId` is unset builds fine and the subscription `HttpClient` is only created when
  resolved, which is the existing `AddManagementClient_WithoutSubscriptionId_ResolvingSubscriptionApiThrows`
  behaviour. Delivery's builder relies on the same fact.
- **`AddHttpClient` calls `AddLogging`**, so `ILogger<T>` resolves to a provider-less logger rather
  than `null`. The handlers' `logger is not null` branches then run against a logger that reports
  nothing enabled. That is the DI path's behaviour already; a user-supplied `ILoggerFactory` must be
  registered *before* `AddSyncClient` so `AddLogging`'s `TryAdd` keeps it, which is the order
  `DeliveryClientBuilder.BuildServices` uses.
- **`ManagementApiFactory.Create` omits resilience**, and `MockClientFactory` builds every domain test
  through it with options that leave `EnableResilience` at its default of `true`. The tests therefore
  run without retries today only because the factory dropped the handler. The container path honours
  the option, so the test helper must set `EnableResilience = false` explicitly or every 5xx fixture
  gains three retries with backoff. Checked: no test under `ManagementClientTests` expects a retry
  through that helper; `CodeSamples/Readme.cs` mentions retry only in a sample that configures its own
  pipeline.
- **`SnapshotOptionsAccessor` has no production caller after the change.** It is used by the two
  factories, the two standalone constructors, and two Sync test files.
- **Delivery already lives with every property of this design**, including the disposal timing,
  the `ValidateOnBuild` provider and the keyed services inside it.

## 3. Design

The container hosts the pipeline. The client owns what it draws from it.

```
Builder.Build()
  ├─ Validator.ValidateObject(options)            → ValidationException, as today
  ├─ new ServiceCollection()
  │    ├─ [ILoggerFactory instance, ILogger<>]     → only when the builder was given one (Sync)
  │    └─ services.Add{Sync|Management}Client(options, configureHttpClient, configureResilience)
  ├─ BuildServiceProvider(ValidateOnBuild, ValidateScopes)
  └─ ServiceCollectionExtensions.CreateOwned{Sync|Management}Client(provider, NamedClients.Default)
       ├─ IHttpClientFactory.CreateClient(httpClientName)   → the HttpClient the client will own
       ├─ RestService.For<TApi>(httpClient, settings)        → shared with the keyed registration
       ├─ MonitorBackedOptionsAccessor(provider, name)       → same accessor the DI path uses
       └─ new Client(api, accessor, ownedResources: OwnedTransport(httpClient(s), provider))
```

Two decisions inside that shape, each taken for one reason:

- **The builder draws the `HttpClient` itself instead of resolving the keyed Refit client.** The keyed
  registration hides the `HttpClient` inside Refit's generated class, and §1.2's disposal invariant
  needs the client to dispose it. The cost is one shared line, `RestService.For` with the default
  settings, moved into a small internal `Create{Sync|Management}Api(HttpClient)` that the keyed
  registration calls too, so there is still one place that knows how a Refit client is made.
- **`ownedResources` is a composite: the `HttpClient`s first, the provider second.** Management's
  private `CompositeDisposable` is the right shape and Sync needs the same one. It moves to
  `src/common/CompositeDisposable.cs` — it references no product type, and after this change it is
  identical in both products, which is the README's bar. Delivery does not take it: its provider
  is the only thing it owns. That difference is kept on purpose, not left over. Sync and Management
  had the disposed-client-fails-at-once invariant before this plan and §1.2 pins it, so they keep
  the `HttpClient` handle that makes it true; Delivery resolves its Refit client from the container,
  never held the `HttpClient`, and never promised more than releasing the container.

The public `ManagementClient(ManagementOptions)` constructor stays. It chains to a private
constructor taking the tuple that a static `BuildOwned(options, configureResilience)` returns, which
is what `BuildDependencies` does today with a different body. The builder's `Build()` calls the same
static, so the constructor and the builder cannot drift.

## 4. Sync — steps

Sync first: one scope, one `HttpClient`, and it has the most exact tests on the standalone path.

1. **`Kontent.Ai.Sync.csproj`**: add `<PackageReference Include="Microsoft.Extensions.DependencyInjection" />`
   and `<Compile Include="$(KontentCommonPath)CompositeDisposable.cs" …>` once step 2 of §6 exists.
2. **`Extensions/ServiceCollectionExtensions.cs`**:
   - Extract `CreateSyncApi(HttpClient httpClient, RefitSettings settings)` from the keyed
     `AddKeyedTransient` lambda; the lambda calls it.
   - Add `internal static SyncClient CreateOwnedSyncClient(ServiceProvider provider, string name)`
     as sketched in §3. It is the only new production code.
3. **`SyncClient.cs`**: delete the four-parameter constructor, `BuildResiliencePipeline` and
   `_ownedHttpClient`; the remaining constructor gains `IDisposable? ownedResources = null` and stores
   it; `Dispose`/`DisposeAsync` become Delivery's: guard, then dispose or `DisposeAsync` the owned
   resource. Rewrite the type remark: the container-built client owns nothing; the builder-built one
   owns its `HttpClient` and the private provider behind it.
4. **`Api/SyncApiFactory.cs`**: delete.
5. **`Configuration/SyncClientBuilder.cs`**: `Build()` becomes §3. Add
   `internal SyncClientBuilder ConfigureHttpClient(Action<IHttpClientBuilder> configure)` — internal,
   `InternalsVisibleTo` already covers the test project — used only by tests to inject a primary
   handler. Rewrite the remarks that say "there is no container to reach into".
6. **Tests**:
   - `StandaloneClientTests`: replace every `new SyncClient(Options(…), primaryHandler: _http, …)`
     (8 sites) with a private helper that goes through `SyncClientBuilder.WithOptions(_ => options)
     .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => _http))`, plus
     `.WithResilience(…)` where the test passed `configureResilience`. Delete the four
     `CreateHttpClient_*` tests and the `BuildHttpClient` helper. `InvalidOptions_AreRejectedAtConstruction`
     becomes a builder call and keeps its assertion. Every other assertion in the file is unchanged.
   - `SyncClientTests`: replace the `SnapshotOptionsAccessor` with a two-line private
     `IOptionsAccessor<SyncOptions>` stub, or an NSubstitute substitute — the file already uses
     NSubstitute for the API.
7. **Gate**: `dotnet test` on Sync green with the snapshot untouched; the eight migrated tests are the
   ones to read line by line in the diff, since they are the proof the chain is the same.

## 5. Management — steps

1. **`Kontent.Ai.Management.csproj`**: the same two additions as Sync's step 1.
2. **`Extensions/ServiceCollectionExtensions.HttpClient.cs`**: extract `CreateApi<T>(HttpClient, RefitSettings)`
   from `RegisterRefitClient`'s keyed lambda, so the builder path and the container path make Refit
   clients through one line.
3. **`Extensions/ServiceCollectionExtensions.cs`**: add
   `internal static (IManagementApi?, ISubscriptionApi?, IDisposable) CreateOwnedApis(ServiceProvider provider, string name)`:
   resolve the options, draw the environment `HttpClient` only when `HasEnvironmentId()` and the
   subscription one only when `HasSubscriptionId()`, build both Refit clients, return them with a
   `CompositeDisposable` over the `HttpClient`s and the provider. This is `BuildDependencies` with the
   chain assembly replaced by two `CreateClient` calls — the scope logic it already has stays.
4. **`ManagementClient.cs`**: `BuildDependencies` becomes `BuildOwned(options, configureResilience)`:
   validate, build the collection and provider, call step 3. Delete `BuildResiliencePipeline` and the
   nested `CompositeDisposable` (moved to common). The public constructor and the internal
   two-parameter one keep their signatures. Rewrite the summary that says the instance owns its
   `HttpClient`s — it still does, and also the provider.
5. **`Api/ManagementApiFactory.cs`**: delete. Its summary calls itself a test-facing factory; the
   test-facing replacement is step 6.
6. **Tests**:
   - `Base/MockClientFactory.cs`: build a private `ServiceCollection`, call
     `AddManagementClient(options, configureHttpClient: b => b.ConfigurePrimaryHttpMessageHandler(() => mock))`
     with **`EnableResilience = false`** on the options (§2), build the provider, resolve the keyed
     `IManagementApi` and `ISubscriptionApi`, and construct the client through the existing internal
     constructor with the injected converter and the provider as `ownedResources`. `CreateForSample`
     goes through the same helper. Nothing about how a test arranges or asserts changes.
   - `Api/IManagementApiSmokeTests.cs:40`: the one direct `ManagementApiFactory.Create` call uses the
     same helper.
   - `ManagementClientBuilderTests` and `DisposeTests`: unchanged. `Dispose_OnCtorBuiltClient_IsIdempotent`
     now disposes a provider; it must still not throw, which is the point of keeping it.
7. **Gate**: `dotnet test` on Management green with the snapshot untouched. The domain tests are the
   regression net here — 970 of them run through `MockClientFactory`, so any drift in base address,
   headers or serialization shows up as a wall of failures rather than a subtle one.

## 6. Shared source — steps

1. **`src/common/OptionsAccessor.cs`**: delete `SnapshotOptionsAccessor<TOptions>` and the sentence in
   the interface remark that describes the snapshot path. `MonitorBackedOptionsAccessor` and the
   interface stay; every handler still takes the interface.
2. **`src/common/CompositeDisposable.cs`**: Management's nested class, verbatim, with the
   `// Shared source` header. Included by Sync and Management only.
3. **`src/common/README.md`**: no rule changes; add the two files to whatever listing exists, if one
   does.

## 7. Documentation — every line that promises the old shape

Each of these was found by grep on the commit above; the plan is not complete until each is rewritten
or confirmed still true.

| Location | Says | Becomes |
|---|---|---|
| `src/sync/README.md:250` | "There is no container involved: the client assembles its own `HttpClient` and handler chain" | The builder builds the same registration as `AddSyncClient` in a private container the client owns; dispose it. |
| `src/sync/README.md:275` | "the only way to bound a container-free client, since the `configureHttpClient` hook is a DI-path feature" | Still true — the builder exposes no `configureHttpClient` — reword "container-free" to "builder-built". |
| `src/sync/README.md:293-295` | "owns its own `HttpClient` … dispose it and that transport is released" | Still true for the `HttpClient`; add the §1.3 sentence about pooled connections. |
| `src/sync/Kontent.Ai.Sync/ISyncClient.cs:7-8` | "a client from `SyncClientBuilder` owns its transport" | Unchanged — still true. |
| `src/sync/Kontent.Ai.Sync/SyncClient.cs:16-21` | "the client builds its own `HttpClient` and owns it" | Owns the `HttpClient` it drew from its private provider, and the provider. |
| `src/sync/Kontent.Ai.Sync/Configuration/SyncClientBuilder.cs:11,17,116` | "there is no container to reach into" | The container is private and disposed with the client. |
| `src/management/README.md:121` | "A standalone client owns its `HttpClient` instances" | Unchanged — still true. |
| `src/management/README.md:138` | "it does not spin up a private service provider" | It does, privately; the built client owns it. Keep "thin wrapper". |
| `src/management/CLAUDE.md:16` | "`ManagementClientBuilder` (fluent, container-free — owns its `HttpClient`s)" | "fluent; builds the DI registration in a private container it owns". |
| `src/management/Kontent.Ai.Management/ManagementClient.cs:36-38` | "owns its `HttpClient`s" | Unchanged, plus the provider. |
| `src/management/Kontent.Ai.Management/Configuration/ManagementClientBuilder.cs:10-19,74` | "there is no container to reach into … does not spin up a private service provider" | Same rewrite as the README line. |
| `src/management/docs/upgrade-guide.md:66,94` | "owns its `HttpClient` instances" | Unchanged — still true. |
| `docs/sdk-plumbing-extraction-analysis.md` §2.6 | proposes this | Point here; done in the same commit as this file. |

## 8. Changelog entries

Both products, under `## Unreleased`, `### Fixed`:

> **Standalone clients are built through the same registration as DI-resolved ones.**
> `…ClientBuilder.Build()` (and, for Management, the `ManagementClient(ManagementOptions)`
> constructor) assembled a second copy of the HTTP pipeline by hand. They now run the same
> `Add…Client` registration inside a private container the built client owns, so a standalone client
> gets what the container path already had: bounded connection lifetimes, so DNS changes are picked up
> by a long-running singleton, and the HTTP client factory's diagnostics when logging is configured.
> Nothing on the public surface changes and the client still owns its `HttpClient` — disposing it
> still fails every further request. One timing difference: pooled connections now close when the
> factory releases the handler rather than at the moment of disposal, which is how a container-resolved
> client has always behaved.

## 9. Abandon criteria

Stop and say so, rather than adapt, if any of these turns out true while implementing:

1. `DisposingTheClient_ReleasesItsHttpClient` cannot be kept green without changing its assertion.
   It should be: a disposed `HttpClient` throws on send whatever owns the handler. If it does not,
   the design's ownership claim is wrong.
2. Either approval snapshot changes by a single line. There is no reason for it to; a change means
   a public member moved.
3. The private provider's `ValidateOnBuild` rejects a registration that the host path accepts. It
   should not — the registrations are identical and Delivery builds the same way — but if it does,
   the fix belongs in the registration, not in the builder, and is a separate change.
4. The §1.3 delta is judged unacceptable for a standalone client. Then the hand-built transport is the
   correct design for these two products and the analysis doc's §2.6 is rewritten to say so, with the
   shared `HandlerChains` extraction reinstated as the way to keep the two copies honest.

## 10. Order and size

1. §6 step 2 (`CompositeDisposable` to common) — pure move, Management unchanged in behaviour.
2. §4 Sync, one commit, tests migrated in the same commit so the tree never has a client without a
   transport.
3. §5 Management, one commit, same rule.
4. §6 step 1, §7, §8 — the cleanup, docs and changelogs, one commit.

Roughly 230 production lines removed, about 60 added, four commits. Every commit: `dotnet build`
in both reference modes, `dotnet test` for the product, snapshot byte-identical.
