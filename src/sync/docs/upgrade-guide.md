# Upgrade Guide: Delivery SDK Sync to Standalone Sync SDK

This guide is for users migrating from the sync functionality included in `Kontent.Ai.Delivery` to the standalone `Kontent.Ai.Sync` package.

Two structural changes matter most:

1. **Sync has its own client.** Use `ISyncClient` (injected independently) instead of calling sync methods on `IDeliveryClient`.
2. **Every call returns `ISyncResult<T>`.** Check `.IsSuccess` / `.Error` before accessing `.Value`. Status, headers, and the continuation token live on the result itself.

## 1. Swap packages

Remove sync-related usage from the Delivery SDK and install the standalone package:

```bash
dotnet add package Kontent.Ai.Sync
```

## 2. Update namespaces

Replace any Delivery SDK sync imports with:

```csharp
using Kontent.Ai.Sync;
using Kontent.Ai.Sync.Abstractions;
```

## 3. Register the sync client separately

Sync is no longer part of the Delivery client. Register it as its own service:

```csharp
services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.ApiMode = ApiMode.Preview;
    options.ApiKey = "your-preview-api-key";
});
```

Inject `ISyncClient` (or `ISyncClientFactory` for named clients) instead of using sync methods on `IDeliveryClient`.

### Method name mapping

```csharp
// Before (on IDeliveryClient)
var initResponse = await deliveryClient.PostSyncV2InitAsync();
var delta = await deliveryClient.GetSyncV2Async(continuationToken);

// After (on ISyncClient)
var initResult = await syncClient.InitializeSyncAsync();
var deltaResult = await syncClient.GetDeltaAsync(syncToken);

// Or fetch all pages in one call:
var allResult = await syncClient.GetAllDeltaAsync(syncToken, maxPages: 10);
```

## 4. Update response access

### Data moved onto `ISyncResult<T>.Value`

Old sync returned the response object directly. The new SDK wraps it in an `ISyncResult<T>` so success, error, status, and the continuation token travel together.

```csharp
// Before
var response = await deliveryClient.GetSyncV2Async(token);
foreach (var item in response.SyncItems) { ... }
var next = response.ApiResponse.ContinuationToken;

// After
var result = await syncClient.GetDeltaAsync(token);
if (!result.IsSuccess)
{
    throw new InvalidOperationException(result.Error?.Message);
}

foreach (var item in result.Value.Items) { ... }
var next = result.SyncToken;
```

The collection property prefix is also dropped: `SyncItems`/`SyncTypes`/`SyncTaxonomies`/`SyncLanguages` become `Items`/`Types`/`Taxonomies`/`Languages` on `ISyncDeltaResponse`.

The SDK does not persist sync tokens. Store `result.SyncToken` after every successful call and pass it into the next `GetDeltaAsync` / `GetAllDeltaAsync`.

### `StatusCode` type changed

`ISyncResult<T>.StatusCode` is now `HttpStatusCode` instead of `int`:

```csharp
using System.Net;

// Before
if (result.StatusCode == 401) { ... }

// After
if (result.StatusCode == HttpStatusCode.Unauthorized) { ... }
```

### `InnerException` renamed to `Exception`

```csharp
// Before
var ex = result.Error?.InnerException;

// After
var ex = result.Error?.Exception;
```

### `ResponseHeaders` added

`ISyncResult<T>.ResponseHeaders` exposes the full HTTP response headers if you need them.

## 5. Named client validation is stricter

If you use named clients, names are now validated at registration time. The following are rejected:

- `null`, empty, or whitespace-only names
- Names containing spaces, including leading or trailing whitespace — use underscores or hyphens instead
- Duplicate names
