# Upgrade Guide: Delivery SDK Sync to Standalone Sync SDK

This guide is for users migrating from the sync functionality included in `Kontent.Ai.Delivery` to the standalone `Kontent.Ai.Sync` package.

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

## 4. Update result handling

### `StatusCode` type changed

`ISyncResult<T>.StatusCode` is now `HttpStatusCode` instead of `int`:

```csharp
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
- Names containing spaces
- Duplicate names
