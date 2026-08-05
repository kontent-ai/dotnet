# Kontent.Ai.Sync

Covers `Kontent.Ai.Sync` and `Kontent.Ai.Sync.Abstractions`, which ship in lockstep
on one version.

Entries before the move to this monorepo were imported from the GitHub Releases of
[kontent-ai/sync-sdk-net](https://github.com/kontent-ai/sync-sdk-net).

## Unreleased

Targets .NET 10. Both packages move from `net8.0` to `net10.0`, which is why this is a major release, and Refit's transport is upgraded across four major versions. Nothing else about the API changes.

### Breaking changes

- **`net8.0` → `net10.0`.** There is no multi-targeting, so a project on .NET 8 cannot install this release at all — restore fails with `NU1202: Package Kontent.Ai.Sync is not compatible with net8.0`. Move to .NET 10 first.
- **`GetAllDeltaAsync` is replaced by `EnumerateDeltaAsync`, which returns `IAsyncEnumerable<ISyncResult<ISyncDeltaResponse>>`.** The old helper decided it had caught up when no collection in a response had reached 100 entries, a threshold published as `SyncConstants.MaxItemsPerEntityType`. The Sync API does not define completion that way — [it defines it as an empty response](https://kontent.ai/learn/docs/apis/sync-api-v2/synchronization#synchronize-changes) — so a response that was merely not full ended the walk, and any changes still queued were skipped until the next synchronization. Nothing was lost, because the returned token stayed valid, but a call that reported success had not necessarily fetched everything. The replacement stops on the empty response the API actually sends, and streams pages instead of buffering every one in memory. Bounding the walk moves to the caller, where `Take` or a `break` replaces `maxPages`.

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

- **`ISyncResult<T>.HasMoreChanges`, `SyncConstants` and `ISyncAllDeltaResult` are removed.** All three existed only to support the threshold above. With completion defined by the API's empty response, the sequence simply ends: there is no "are there more" flag to read, and no client-side page-size constant to keep in step with the server. Carry `SyncToken` from the last yielded result; when nothing is yielded, the token you passed in is still current.
- **The `configureRefit` parameter is gone from all three `AddSyncClient` overloads.** The hook exposed the transport library's settings object, but everything reachable through it was load-bearing rather than configurable — the parameter-key formatter matches the API's casing, and the serializer options carry the converters the wire format requires. Overriding them broke requests silently. Delete the argument; the SDK's own tests only ever used it to assert the callback fired.

### Changed

- **Cancellation now throws; other transport failures are results.** Refit's upgrade changed the
  contract: exceptions raised in the HTTP pipeline are captured into the response rather than thrown.
  A network failure, DNS failure or resilience-pipeline rejection is therefore an unsuccessful result
  carrying the exception, consistent with how every other failure in this SDK is reported. Cancellation
  is the exception to that: when the caller's token fires, the `OperationCanceledException` is rethrown,
  so `Task.IsCanceled`, `Task.WhenAll` and cancellation handlers behave as they do everywhere else in
  .NET. Previously **all** of these threw.
- **Transport failures report status `0`.** `ISyncResult` now carries `(HttpStatusCode)0` for that case rather than an invented code. Responses that did arrive are unaffected.

### Fixed

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
