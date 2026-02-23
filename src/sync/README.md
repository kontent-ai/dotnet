# Kontent.ai Sync SDK for .NET

[![NuGet](https://img.shields.io/nuget/v/Kontent.Ai.Sync?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Sync)
[![License](https://img.shields.io/github/license/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/blob/master/LICENSE)
[![Contributors](https://img.shields.io/github/contributors/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/graphs/contributors)
[![Last Commit](https://img.shields.io/github/last-commit/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/commits/master)
[![Issues](https://img.shields.io/github/issues/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/issues)

Official .NET SDK for the [Kontent.ai Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api-v2/).

Use this SDK to initialize sync and process delta updates for content items, content types, languages, and taxonomies.

## Installation

```bash
dotnet add package Kontent.Ai.Sync
```

## Quick Start

### 1. Register the sync client

```csharp
using Kontent.Ai.Sync;
using Kontent.Ai.Sync.Abstractions;

services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.ApiMode = ApiMode.Preview;
    options.ApiKey = "your-preview-api-key";
});
```

### 2. Initialize sync

```csharp
public sealed class SyncService(ISyncClient syncClient)
{
    public async Task<string?> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await syncClient.InitializeSyncAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "Sync init failed.");
        }

        // Persist and reuse this token for subsequent delta calls.
        return result.SyncToken;
    }
}
```

### 3. Fetch delta updates

```csharp
var deltaResult = await syncClient.GetDeltaAsync(syncToken, cancellationToken);

if (!deltaResult.IsSuccess)
{
    Console.WriteLine($"Sync failed: {deltaResult.Error?.Reason} - {deltaResult.Error?.Message}");
    return;
}

var delta = deltaResult.Value;
foreach (var item in delta.Items)
{
    Console.WriteLine($"Item change: {item.ChangeType}");
}

await SaveSyncTokenAsync(deltaResult.SyncToken);
```

### 4. Fetch all pages automatically

```csharp
var allResult = await syncClient.GetAllDeltaAsync(syncToken, maxPages: 10, cancellationToken);

if (allResult.IsSuccess)
{
    Console.WriteLine($"Fetched {allResult.PagesFetched} pages.");
    Console.WriteLine($"Final token: {allResult.FinalSyncToken}");
}
```

## Configuration

### API modes

```csharp
// Public Production API
services.AddSyncClient(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.ApiMode = ApiMode.Public;
});

// Preview API
services.AddSyncClient(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.ApiMode = ApiMode.Preview;
    o.ApiKey = "preview-api-key";
});

// Secure Production API
services.AddSyncClient(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.ApiMode = ApiMode.Secure;
    o.ApiKey = "secure-access-api-key";
});
```

### Builder API

```csharp
services.AddSyncClient(builder => builder
    .WithEnvironmentId("your-environment-id")
    .UsePreviewApi("preview-api-key")
    .DisableRetryPolicy()
    .Build());
```

### Configuration binding

`appsettings.json`:

```json
{
  "SyncOptions": {
    "EnvironmentId": "your-environment-id",
    "ApiMode": "Preview",
    "ApiKey": "preview-api-key",
    "EnableResilience": true
  }
}
```

Registration:

```csharp
services.AddSyncClient(configuration.GetSection("SyncOptions"));
```

## Named Clients

```csharp
services.AddSyncClient("production", o =>
{
    o.EnvironmentId = "prod-environment-id";
    o.ApiMode = ApiMode.Public;
});

services.AddSyncClient("preview", o =>
{
    o.EnvironmentId = "preview-environment-id";
    o.ApiMode = ApiMode.Preview;
    o.ApiKey = "preview-api-key";
});

public sealed class MultiEnvironmentService(ISyncClientFactory factory)
{
    public ISyncClient DefaultClient => factory.Get();
    public ISyncClient PreviewClient => factory.Get("preview");
}
```

## Error Handling

`ISyncResult<T>` uses explicit success/failure signaling.

```csharp
var result = await syncClient.GetDeltaAsync(syncToken);

if (!result.IsSuccess)
{
    Console.WriteLine(result.Error?.Reason);
    Console.WriteLine(result.Error?.Message);
    Console.WriteLine(result.StatusCode);
    return;
}
```

Important fields:
- `ISyncResult<T>.StatusCode` (`HttpStatusCode`)
- `ISyncResult<T>.ResponseHeaders`
- `IError.Reason`
- `IError.Exception`

## Token Persistence

The SDK does not persist sync tokens. Store `SyncToken` after every successful call and pass it into the next `GetDeltaAsync` or `GetAllDeltaAsync` call.

## Upgrade and Release Docs

- Upgrade guide: `docs/upgrade-guide.md`
- Release readiness checklist: `docs/release-readiness-checklist.md`
- 1.0.0 release notes draft: `docs/release-notes-1.0.0-draft.md`

## License

Licensed under the MIT License. See `LICENSE.md` for details.
