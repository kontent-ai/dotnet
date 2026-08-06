# Upgrade Guide: Kontent.Ai.Sync 1.0 → 2.0

This guide covers upgrading from `Kontent.Ai.Sync` **1.0.0** to **2.0.0**.

Coming from the sync functionality that used to live in `Kontent.Ai.Delivery`? Read
[the standalone-SDK guide](upgrade-guide.md) first — it covers that move, and this guide picks up after it.

Two changes account for nearly all the work: the framework moves to .NET 10, and paging through the
sync feed is now a stream you enumerate rather than a call that returns everything at once. The rest is
small and the compiler points at each one.

## At a glance

| Change | Effort |
|---|---|
| `net8.0` → `net10.0` | Move your project to .NET 10 first |
| `Kontent.Ai.Sync.Abstractions` folded into `Kontent.Ai.Sync` | Drop the reference, change one `using`; see [§2](#2-one-package-one-namespace) |
| `ISyncItem`/`ISyncType`/`ISyncLanguage`/`ISyncTaxonomy` → `SyncChange<TData>` | Change the loop variable's type; `Data` is now typed. See [§3](#3-one-delta-type-with-a-typed-payload) |
| `GetAllDeltaAsync` → `EnumerateDeltaAsync` | Rewrite the loop; see [§4](#4-paging-is-now-a-stream) |
| `HasMoreChanges`, `SyncConstants`, `ISyncAllDeltaResult` removed | Delete the usage; the stream ends on its own |
| `SyncToken` is non-nullable | Drop any `?? previous` fallback |
| `InitializeSyncAsync` returns `ISyncResult` | Change the declared type, or use `var` |
| `IDisposable` off the client interfaces | Keep the builder result as `var` |
| `SyncClientBuilder.ConfigureServices` → `WithResilience` | Replace the call |
| `configureRefit` parameter removed | Delete the argument |

## 1. Target .NET 10

There is no multi-targeting. A project on .NET 8 cannot install 2.0 at all — restore fails with
`NU1202: Package Kontent.Ai.Sync is not compatible with net8.0`.

```xml
<TargetFramework>net10.0</TargetFramework>
```

## 2. One package, one namespace

`Kontent.Ai.Sync.Abstractions` no longer exists. Everything it held — `ISyncClient`, `ISyncResult`,
the model interfaces, `SyncOptions`, `ApiMode` — now ships inside `Kontent.Ai.Sync`, in the
`Kontent.Ai.Sync` namespace. Type names and members are unchanged.

The split was there so the contracts could be taken without the client. Nothing ever did: the only
consumer of the package was `Kontent.Ai.Sync` itself.

Remove the package reference:

```xml
<PackageReference Include="Kontent.Ai.Sync" Version="2.0.0" />
<!-- delete this line -->
<PackageReference Include="Kontent.Ai.Sync.Abstractions" Version="1.0.0" />
```

and drop the `using`:

```csharp
using Kontent.Ai.Sync;
- using Kontent.Ai.Sync.Abstractions;
```

There is no type-forwarding shim, deliberately: leaving the old package resolvable would mean
shipping an assembly nobody maintains. A stale reference fails at compile time with a missing-type
error naming exactly what to remove, rather than resolving to something frozen at 1.0.

## 3. One delta type, with a typed payload

The four entry interfaces are gone. Each declared the same two members, and each was backed by an
identical record — while the part that genuinely differs, the payload, sat behind `object?`.

```csharp
// Before
foreach (ISyncItem item in page.Value.Items)
{
    object? data = item.Data;   // a JsonElement at runtime; parse it yourself
}

// After
foreach (SyncChange<SyncItemData> item in page.Value.Items)
{
    string? codename = item.Data?.System.Codename;
    DateTime when = item.Timestamp;
}
```

`var` in the loop keeps this to a no-op. What changes is that `Data` is now a modelled type rather
than an object you had to deserialize by hand, and that `Timestamp` exists at all — see [§4 of the
changelog](../CHANGELOG.md) for why it was missing.

The four payloads are separate types on purpose. A content item's `system` carries `collection`,
`language`, `type` and workflow state; a language's carries three properties and no `last_modified`.
`SyncTypeData` and `SyncTaxonomyData` are identical today and still separate, because the API can
extend either on its own.

`Data` stays nullable: a deletion may arrive without a payload.

## 4. Paging is now a stream

`GetAllDeltaAsync` buffered every page into memory and returned them together. It also decided it had
caught up when no collection in a response had reached 100 entries — a threshold the API never
promised. The [Sync API defines completion as an empty response](https://kontent.ai/learn/docs/apis/sync-api-v2/synchronization#synchronize-changes),
so the old walk stopped one request before the API had confirmed the feed was drained.

`EnumerateDeltaAsync` stops on the empty response and yields pages as they arrive.

```csharp
// Before
var all = await syncClient.GetAllDeltaAsync(syncToken, maxPages: 10);

if (all.IsSuccess)
{
    foreach (var page in all.Responses)
    {
        Process(page);
    }
}

var next = all.FinalSyncToken;
```

```csharp
// After
var token = syncToken;

await foreach (var page in syncClient.EnumerateDeltaAsync(syncToken, cancellationToken).Take(10))
{
    if (!page.IsSuccess)
    {
        logger.LogError("Sync failed: {Error}", page.Error?.Message);
        break;
    }

    Process(page.Value);
    token = page.SyncToken;
}
```

Three things to carry across:

- **`maxPages` is gone.** Bound the walk yourself with `Take(n)` or by `break`ing. Nothing is fetched
  ahead of what you consume, so stopping early costs nothing.
- **Failure arrives as a yielded result**, not an exception, and ends the sequence. Check `IsSuccess`
  inside the loop.
- **An empty sequence means you were already up to date.** There is no newer token in that case, so the
  token you passed in is still current — seed your variable with it, as above.

Where you persist the token is a choice worth making deliberately. Saving once after the loop means a
crash part-way through reprocesses from the previous token: some changes arrive twice, none are missed.
Saving after each page resumes closer to where you stopped. Saving *before* processing a page is the one
variant that can lose work.

## 5. `HasMoreChanges`, `SyncConstants`, `ISyncAllDeltaResult` are gone

All three existed to support the 100-entry threshold. With completion defined by the API's empty
response, the sequence simply ends — there is no flag to poll and no page-size constant to keep in step
with the server.

```csharp
// Before
var result = await syncClient.GetDeltaAsync(token);
if (result.HasMoreChanges) { /* fetch again */ }

// After
await foreach (var page in syncClient.EnumerateDeltaAsync(token)) { /* runs until drained */ }
```

If you are driving the loop yourself with `GetDeltaAsync`, stop when every collection comes back empty.

## 6. `SyncToken` is non-nullable

The API issues a fresh token with every successful response, and it is the only way to make the next
request. A successful response without one now fails where the response is mapped — an
`InvalidOperationException` naming the request — rather than handing back a result you could not
continue from. In exchange, `SyncToken` is declared `string` rather than `string?`.

```csharp
- token = page.SyncToken ?? token;
+ token = page.SyncToken;
```

Code that only reads the token after checking `IsSuccess` needs no change.

## 7. `InitializeSyncAsync` returns `ISyncResult`

Initialization establishes a starting point rather than returning content, so it no longer carries a
value. `ISyncInitResponse` — an interface with no members — is removed, and `ISyncResult` is new and
non-generic. `ISyncResult<T>` now derives from it, adding only `Value`.

```csharp
- ISyncResult<ISyncInitResponse> init = await syncClient.InitializeSyncAsync();
+ ISyncResult init = await syncClient.InitializeSyncAsync();   // or var

await SaveTokenAsync(init.SyncToken);   // unchanged
```

`GetDeltaAsync` and `EnumerateDeltaAsync` are untouched, `page.Value` included.

## 8. Disposal moved to the concrete client

`ISyncClient` no longer carries `IDisposable` / `IAsyncDisposable`. A client resolved from a container is
owned by the container and was never yours to dispose; a client from `SyncClientBuilder` owns its
`HttpClient` and is returned as the concrete `SyncClient`, which is disposable.

```csharp
// Unchanged — and what every example uses
await using var client = SyncClientBuilder.WithOptions(...).Build();

// Breaks: the interface no longer has Dispose
- ISyncClient client = SyncClientBuilder.WithOptions(...).Build();
- client.Dispose();
+ var client = SyncClientBuilder.WithOptions(...).Build();
+ client.Dispose();
```

Container-resolved clients are still disposed by the container, which checks the runtime type rather than
the registered service type.

## 9. `SyncClientBuilder.ConfigureServices` → `WithResilience`

The builder no longer stands up a private service container — the client constructs its handler chain
directly and owns the resulting `HttpClient`. `ConfigureServices` existed only to reach into that
container. Replacing the resilience pipeline was the realistic use, and that is now first-class.

```csharp
// Before
.ConfigureServices(services => { /* replace registrations */ })

// After
.WithResilience(builder => builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 5 }))
```

## 10. `configureRefit` is gone

Removed from all three `AddSyncClient` overloads. Everything reachable through it was load-bearing rather
than configurable — the parameter-key formatter matches the API's casing, and the serializer options
carry the converters the wire format requires. Delete the argument.

## What did not change

- `GetDeltaAsync`'s signature and payload, including `Value.Items` / `.Types` / `.Languages` / `.Taxonomies`.
- `IsSuccess` / `Error` / `StatusCode` / `RequestUrl` / `ResponseHeaders` on every result.
- Registration by `Action<SyncOptions>`, `IConfiguration`, options instance or options builder — all still
  present, and the configuration overloads gained the `configureHttpClient` / `configureResilience` hooks
  they were missing.
- `ISyncClientFactory` and named clients.
- Source tracking via `SyncSourceTrackingHeaderAttribute`.

## Full list of changes

See the [changelog](../CHANGELOG.md) for everything in this release, including the non-breaking additions
and the fixes to tracking-header handling.
