# Kontent.Ai.Sync

Covers `Kontent.Ai.Sync` and `Kontent.Ai.Sync.Abstractions`, which ship in lockstep
on one version.

Entries before the move to this monorepo were imported from the GitHub Releases of
[kontent-ai/sync-sdk-net](https://github.com/kontent-ai/sync-sdk-net).

## Unreleased

Targets .NET 10. Both packages move from `net8.0` to `net10.0`, and Refit's transport is upgraded across
four major versions.

The API changes too. Paging through the sync feed becomes a stream you enumerate, terminating on the
signal the API actually sends rather than on an inferred page size; the result contract splits so that
initialization stops pretending to return content; and disposal moves off the client interface onto the
client that owns resources. Most consumers touch one loop and nothing else.

See the [1.0 → 2.0 upgrade guide](docs/upgrade-guide-1.0-to-2.0.md) for the migration, change by change.

### Breaking changes

- **`net8.0` → `net10.0`.** There is no multi-targeting, so a project on .NET 8 cannot install this release at all — restore fails with `NU1202: Package Kontent.Ai.Sync is not compatible with net8.0`. Move to .NET 10 first.
- **`InitializeSyncAsync` returns `ISyncResult` instead of `ISyncResult<ISyncInitResponse>`, and `ISyncInitResponse` is removed.** Initialization establishes a starting point rather than returning content — the useful output has always been the token, on `SyncToken`. `ISyncInitResponse` was an interface with no members, so `Value` was an object you could hold but never read. `ISyncResult` is new and non-generic, and `ISyncResult<T>` now derives from it, adding only `Value`; this mirrors `IManagementResult` / `IManagementResult<T>` in the Management SDK. Every other member is unchanged and still reachable on both.

  ```csharp
  // Before
  ISyncResult<ISyncInitResponse> init = await syncClient.InitializeSyncAsync();

  // After — or simply var, as every example in the README uses
  ISyncResult init = await syncClient.InitializeSyncAsync();
  await SaveTokenAsync(init.SyncToken);   // unchanged
  ```

  `GetDeltaAsync` and `EnumerateDeltaAsync` are untouched, including `page.Value.Items`. Initialization also no longer deserializes a response body: success now depends on the status code and the continuation token alone, so an unreadable body on an endpoint whose body is irrelevant can no longer fail the call.
- **The client interfaces no longer carry `IDisposable` / `IAsyncDisposable`; the concrete clients do.** Disposal exists for one situation - a client built outside a container, which owns its own transport and must release it. Putting it on the interface meant every consumer holding `ISyncClient` was offered a `Dispose()` that, on the container path, released nothing and must not be called: the container owns that lifetime. `SyncClientBuilder.Build()` now returns the concrete `SyncClient`, which is `IDisposable` and `IAsyncDisposable`, so disposal stays available exactly where it means something.

  `await using var client = SyncClientBuilder…Build();` is unchanged, and so is every DI usage. The only code that breaks widened the builder result to the interface and then disposed it:

  ```csharp
  // Before - no longer compiles, because the interface has no Dispose
  ISyncClient client = SyncClientBuilder…Build();
  client.Dispose();

  // After - keep the concrete type, or just use var
  var client = SyncClientBuilder…Build();
  client.Dispose();
  ```

  Container-resolved clients are still disposed by the container, which checks the runtime type rather than the registered service type - so nothing changes there.
- **`SyncClientBuilder.ConfigureServices` is removed, replaced by `WithResilience`.** Building a client outside dependency injection no longer stands up a private service container: the client constructs the handler chain directly and owns the resulting `HttpClient`, matching how `ManagementClientBuilder` already worked. `ConfigureServices` existed only to reach into that container and has nothing left to configure. Replacing the resilience pipeline was the one thing it was realistically used for, so that is now a first-class `WithResilience(...)`, mirroring the DI overloads and the Management builder. Everything else is unchanged.

  This also removes a layer that existed only to make disposal work. Previously `Build()` returned a wrapper whose sole job was to delegate every method and dispose the container behind it; now the client itself owns its `HttpClient`, so disposing it releases exactly what it created. Clients resolved from a container are unaffected: the container owns their transport, and disposing one releases nothing.
- **`GetAllDeltaAsync` is replaced by `EnumerateDeltaAsync`, which returns `IAsyncEnumerable<ISyncResult<ISyncDeltaResponse>>`.** The old helper decided it had caught up when no collection in a response had reached 100 entries, a threshold published as `SyncConstants.MaxItemsPerEntityType`. The Sync API defines completion differently — [as an empty response](https://kontent.ai/learn/docs/apis/sync-api-v2/synchronization#synchronize-changes) — so the walk ended one request before the API had confirmed the feed was drained, on a condition inferred from a page size rather than read from the signal the API sends. The replacement stops on the empty response, and streams pages instead of buffering every one in memory. Bounding the walk moves to the caller, where `Take` or a `break` replaces `maxPages`.

  ```csharp
  // Before
  var all = await syncClient.GetAllDeltaAsync(syncToken, maxPages: 10);
  foreach (var page in all.Responses) { /* ... */ }

  // After
  await foreach (var page in syncClient.EnumerateDeltaAsync(syncToken).Take(10))
  {
      if (!page.IsSuccess) break;
      /* page.Value */
  }
  ```

- **`ISyncResult<T>.SyncToken` is no longer nullable, and a successful response without an `X-Continuation` header now throws.** The API issues a fresh token with every initialization and every delta, and it is the only way to make the next request — so a successful response without one leaves a caller holding data it can never continue from. That is now refused where the response is mapped, which covers `InitializeSyncAsync`, `GetDeltaAsync` and `EnumerateDeltaAsync` alike, with an `InvalidOperationException` naming the request. In exchange `SyncToken` is declared `string` rather than `string?`, matching `Value`: both are meaningful only when `IsSuccess` is true. Code that wrote `result.SyncToken ?? previous` can drop the fallback — it was dead once the guarantee existed. Nothing changes for callers that only read the token after checking `IsSuccess`.
- **`ISyncResult<T>.HasMoreChanges`, `SyncConstants` and `ISyncAllDeltaResult` are removed.** All three existed only to support the threshold above. With completion defined by the API's empty response, the sequence simply ends: there is no "are there more" flag to read, and no client-side page-size constant to keep in step with the server. Carry `SyncToken` from the last yielded result; when nothing is yielded, the token you passed in is still current.
- **The `configureRefit` parameter is gone from all three `AddSyncClient` overloads.** The hook exposed the transport library's settings object, but everything reachable through it was load-bearing rather than configurable — the parameter-key formatter matches the API's casing, and the serializer options carry the converters the wire format requires. Overriding them broke requests silently. Delete the argument; the SDK's own tests only ever used it to assert the callback fired.

### Changed

- **Registering from `IConfiguration` can now customize the HTTP client and the resilience pipeline.** The configuration-based `AddSyncClient` overloads took no `configureHttpClient` / `configureResilience`, so binding options from configuration and replacing the retry pipeline were mutually exclusive. The workaround — binding by hand inside an `Action<SyncOptions>` — compiles and looks equivalent, but registers no change token, so `IOptionsMonitor` silently stops reloading. Both hooks are now available on every configuration overload, alongside the ones that already had them.
- **`AddSyncClient` gained the overloads its sibling SDKs already had**, so the three register a client the same way: options configured with access to the `IServiceProvider`, in both named and unnamed form. Nothing was removed, and existing calls are unaffected — this closes gaps rather than reshaping the surface.
- **`SyncOptions.DefaultConfigurationSectionName`** exposes the section name the configuration overloads bind by default, matching `ManagementOptions`. Design-time tools that resolve the SDK's configuration from the same sources can probe it instead of hard-coding the string.
- **Cancellation now throws; other transport failures are results.** Refit's upgrade changed the
  contract: exceptions raised in the HTTP pipeline are captured into the response rather than thrown.
  A network failure, DNS failure or resilience-pipeline rejection is therefore an unsuccessful result
  carrying the exception, consistent with how every other failure in this SDK is reported. Cancellation
  is the exception to that: when the caller's token fires, the `OperationCanceledException` is rethrown,
  so `Task.IsCanceled`, `Task.WhenAll` and cancellation handlers behave as they do everywhere else in
  .NET. Previously **all** of these threw. An expired `HttpClient.Timeout` is *not* cancellation, even
  though .NET surfaces it as a `TaskCanceledException`: the request was sent, so it is reported as a
  failed result like any other transport failure.
- **Transport failures report status `0`.** `ISyncResult` now carries `(HttpStatusCode)0` for that case rather than an invented code. Responses that did arrive are unaffected.

### Fixed

- **The configured retry pipeline can now run to completion.** `HttpClient`'s 100-second default bounds the *whole* call, retries and backoff included, and nothing raised it — so a pipeline allowed four 30-second attempts plus exponential backoff was silently cut off partway through the last one. The SDK's resilience pipeline already bounds each attempt, so it now owns timing outright and the transport-level ceiling is removed. Requests still stop when your `CancellationToken` fires.
- **Long-running applications pick up DNS changes instead of pinning the address resolved at startup.** The registered client is a singleton and takes its `HttpClient` from `IHttpClientFactory` once, so the handler chain it holds was never rotated — the factory only hands a fresh chain to a *new* `CreateClient` call. Connections now recycle every two minutes, matching the factory's own default handler lifetime. A sync walk is exactly the kind of process that stays up long enough for this to matter. Configuring your own primary handler via `configureHttpClient` still overrides this, as before.
- **Responses are now disposed once mapped, instead of every request leaking one until finalization.** Each call held its `HttpResponseMessage` open for the garbage collector to reclaim, which on a sync walk means one per page. Nothing the result carries is affected — Refit buffers the content, and header collections outlive disposal.
- **A failure resolving the `X-KC-SOURCE` header no longer breaks every later request.** The value is cached in a `Lazy<string?>`, and the resolution walks the call stack to attribute the calling package. An exception thrown during that walk was cached alongside the value and rethrown on every subsequent request for the lifetime of the process. Resolution failures are now contained and the header simply omitted, which was the intent.
- **An assembly with no informational version reports `0.0.0` in tracking headers rather than an empty version.** The build-metadata stripping ran after the fallback rather than before it, so a blank version survived as an empty string and travelled into the header. Only reachable for an assembly built without version attributes, which is not the case for anything this SDK ships.

### Dependencies

Shipped floors on `Kontent.Ai.Sync` moved up, all .NET 10 aligned:

- `Microsoft.Extensions.*` (`Configuration`, `.Binder`, `Logging.Abstractions`, `Options`, `.ConfigurationExtensions`, `.DataAnnotations`) **9.0.15** → **10.0.10**.
- `Microsoft.Extensions.Http.Resilience` **9.6.0** → **10.8.0**.
- `Refit` and `Refit.HttpClientFactory` **10.2.0** → **14.0.1**.

`Kontent.Ai.Sync.Abstractions` ships no package dependencies of its own and is unaffected.

### Internal

Refit 14 builds request logic at compile time rather than by reflection. `ISyncApi` generates completely and gained that with no changes.


## 1.0.0 (2026-04-16)

First stable release of the **Kontent.ai Sync SDK for .NET**, targeting the [Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api-v2/).

### What's Changed Since 1.0.0-rc1

#### New Features

- **Standalone client**: `SyncClientBuilder` constructs a fully-wired `ISyncClient` without requiring a host DI container. Suitable for console apps, Azure Functions isolated workers, scripts, and tests. The returned client owns its dependencies and must be disposed.
- **Downstream source tracking**: `[assembly: SyncSourceTrackingHeaderAttribute]` lets libraries built on the Sync SDK opt into `X-KC-SOURCE` ecosystem analytics, mirroring the Delivery SDK's convention.

#### Fixes

- **`X-KC-SOURCE` header value** — previously echoed the SDK's own `X-KC-SDKID`, which is reserved for downstream tools. The header is now omitted unless a caller assembly declares `SyncSourceTrackingHeaderAttribute`.
- **Commit SHA leak** — SourceLink embeds the build commit SHA into `AssemblyInformationalVersion` (e.g. `1.0.0+<sha>`). The tracking headers now strip the `+metadata` suffix so only the semantic version reaches Kontent.ai.
- **Transitive CVE fix** — pinned `System.Text.Json` to 8.0.5 to address two High-severity advisories in the 8.0.0 transitive version ([GHSA-hh2w-p6rv-4g7w](https://github.com/advisories/GHSA-hh2w-p6rv-4g7w), [GHSA-8g4q-xg66-9fp4](https://github.com/advisories/GHSA-8g4q-xg66-9fp4)).

#### Improvements

- **CI vulnerability audit** — `dotnet list package --vulnerable` now runs on every PR/push and fails the build on any reported advisory.
- **Expanded test coverage** — resilience pipeline classifiers, `Retry-After` delta respect, trusted-host auth stripping with custom endpoints, and tracking header composition (including SHA-strip regression guard).
- **Explicit `PackageId`** declared on both shippable projects for defensive metadata.

### Migration from Legacy (Delivery SDK-based) Sync

See [`docs/upgrade-guide.md`](upgrade-guide.md) for step-by-step migration instructions.

### Installation

```bash
dotnet add package Kontent.Ai.Sync --version 1.0.0
```

### Requirements

- .NET 8.0 or later
- Kontent.ai environment with Sync API access

**Full Changelog**: https://github.com/kontent-ai/sync-sdk-net/compare/1.0.0-rc1...1.0.0

## 1.0.0-rc1 (2026-02-24)

First release candidate of the **Kontent.ai Sync SDK for .NET**, targeting the [Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api-v2/).

> [!IMPORTANT]
> This is a release candidate. The API surface is considered stable, but breaking changes may still occur before the final 1.0.0 release.

### What's Changed Since 0.0.3

#### Breaking Changes

- `ISyncResult<T>.StatusCode` changed from `int` to `HttpStatusCode`
- `IError.InnerException` replaced by `IError.Exception`
- `IError.Reason` and `SyncErrorReason` enum removed — use `ErrorCode`/`SpecificCode` instead
- `Kontent.Ai.Sync.Extensions` namespace removed — use `Kontent.Ai.Sync` for all `AddSyncClient` extensions

#### New Features

- **Configuration binding**: `AddSyncClient(IConfigurationSection)` overload for `appsettings.json` support
- **Builder API**: Fluent configuration with `WithEnvironmentId()`, `UsePreviewApi()`, `UseSecureApi()`, and more
- **Default client access**: `ISyncClientFactory.Get()` without a name returns the default client
- **Response headers**: `ISyncResult<T>.ResponseHeaders` exposes full HTTP response headers
- **Input validation**: Builder methods reject null/empty/whitespace values at registration time

#### Improvements

- Resilience pipeline aligned with Delivery SDK standards, including `Retry-After` support for `429`
- Stricter named client validation — null, empty, whitespace, and duplicate names are rejected at registration

### Migration from legacy, delivery-sdk-based sync functionality

See [`docs/upgrade-guide.md`](upgrade-guide.md) for step-by-step migration instructions.

### Installation

```bash
dotnet add package Kontent.Ai.Sync --version 1.0.0-rc1
```

### Requirements

- .NET 8.0 or later
- Kontent.ai environment with Sync API access


**Full Changelog**: https://github.com/kontent-ai/sync-sdk-net/compare/0.0.3...1.0.0-rc1

## 0.0.3 (2025-11-13)

### What's changed?

- Fixed faulty enum and interface deserialization resulting in deltas not being deserialized properly

**Full Changelog**: https://github.com/kontent-ai/sync-sdk-net/compare/0.0.2...0.0.3

## 0.0.2 (2025-11-07)

This is the first standalone release of the **Kontent.ai Sync SDK for .NET**, enabling efficient synchronization of content changes from your Kontent.ai projects using the [Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api/).

>[!IMPORTANT]
>This SDK is released as beta. Breaking changes may happen before official release.

>[!NOTE]
>Version 0.0.1 was missing an important commit due to human (mine...) error. 0.0.2 is to be treated as the first working release.

### What's New

This is the first standalone release of the **Kontent.ai Sync SDK for .NET**, enabling efficient synchronization of content changes from your Kontent.ai projects using the [Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api-v2/).

### What's New

Previously, Sync API functionality was included in the Delivery SDK (last available in [v18.3.0](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/18.3.0)). Starting with Delivery SDK v19, sync functionality has been extracted into this dedicated package to:

- Provide a focused, lightweight SDK specifically for synchronization workflows
- Enable independent versioning and feature development
- Support Sync API v2 exclusively (v1 deprecated)

### Key Features

- **Three API modes**: Public, Preview, and Secure production access
- **Automatic pagination**: `GetAllDeltaAsync` helper for fetching all changes
- **Structured error handling**: Categorized error reasons for precise error handling
- **Resilience built-in**: Retry policies with exponential backoff using Polly
- **Multi-tenant support**: Named clients via factory pattern

### Installation

```bash
dotnet add package Kontent.Ai.Sync
```

### Migration from Delivery SDK

If you're currently using sync functionality from Delivery SDK v18.x, refer to the [README](README.md) for complete setup instructions with the new standalone SDK.

### Requirements

- .NET 8.0 or later
- Kontent.ai environment with Sync API access
