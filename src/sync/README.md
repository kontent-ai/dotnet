# Kontent.ai Sync SDK for .NET

[![NuGet](https://img.shields.io/nuget/v/Kontent.Ai.Sync?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Sync)
[![License](https://img.shields.io/github/license/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/blob/main/LICENSE.md)
[![Contributors](https://img.shields.io/github/contributors/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/graphs/contributors)
[![Last Commit](https://img.shields.io/github/last-commit/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/commits/main)
[![Issues](https://img.shields.io/github/issues/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/issues)

Official .NET SDK for the [Kontent.ai Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api-v2/).

Use this SDK to initialize sync and process delta updates for content items, content types, languages, and taxonomies.

> [!IMPORTANT]
> This SDK targets **Sync API v2** exclusively. Sync API v1 is deprecated and not supported.

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
    Console.WriteLine($"Sync failed: {deltaResult.Error?.Message} (request {deltaResult.Error?.RequestId})");
    return;
}

var delta = deltaResult.Value;
foreach (var item in delta.Items)
{
    Console.WriteLine($"Item change: {item.ChangeType}");
}

await SaveSyncTokenAsync(deltaResult.SyncToken);

// If more pages are pending, keep calling GetDeltaAsync with the new token,
// or switch to GetAllDeltaAsync to auto-paginate.
if (deltaResult.HasMoreChanges)
{
    // ...
}
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

### Options builder

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

## Standalone client (without DI)

For console apps, Azure Functions isolated workers, scripts, or tests where a full DI container is not available, use `SyncClientBuilder` to construct a client directly. The builder spins up a private service collection internally; the returned client owns its dependencies and must be disposed.

```csharp
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Configuration;

await using var client = SyncClientBuilder
    .WithOptions(opts => opts
        .WithEnvironmentId("your-environment-id")
        .UsePreviewApi("preview-api-key")
        .Build())
    .Build();

var result = await client.InitializeSyncAsync();
```

Optional configuration:

```csharp
await using var client = SyncClientBuilder
    .WithOptions(opts => opts.WithEnvironmentId("env-id").UseProductionApi().Build())
    .WithLoggerFactory(loggerFactory)
    .ConfigureServices(services =>
    {
        // Register or replace any service on the internal container.
    })
    .Build();
```

The returned client is thread-safe and should be used as a singleton for the lifetime of your application. Each `Build()` call creates an independent client with its own HTTP client.

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
    public ISyncClient ProductionClient => factory.Get("production");
    public ISyncClient PreviewClient => factory.Get("preview");
}
```

## Error Handling

`ISyncResult<T>` uses explicit success/failure signaling.

```csharp
var result = await syncClient.GetDeltaAsync(syncToken);

if (!result.IsSuccess)
{
    Console.WriteLine(result.Error?.Message);
    Console.WriteLine(result.Error?.RequestId);
    Console.WriteLine(result.Error?.ErrorCode);
    Console.WriteLine(result.StatusCode);
    return;
}
```

Important fields:
- `ISyncResult<T>.StatusCode` (`HttpStatusCode`)
- `ISyncResult<T>.ResponseHeaders`
- `ISyncResult<T>.RequestUrl`
- `ISyncResult<T>.HasMoreChanges`
- `IError.Message`
- `IError.RequestId`
- `IError.ErrorCode` / `IError.SpecificCode`
- `IError.Exception`

## Token Persistence

The SDK does not persist sync tokens. Store `SyncToken` after every successful call and pass it into the next `GetDeltaAsync` or `GetAllDeltaAsync` call.

## Source Tracking (for Tool Authors)

Every request the SDK sends carries two tracking headers:

- **`X-KC-SDKID`** — identifies this SDK. Always set to `nuget.org;Kontent.Ai.Sync;<version>`. You can't configure it.
- **`X-KC-SOURCE`** — identifies a library built *on top of* the SDK. Only set when a caller assembly opts in via `SyncSourceTrackingHeaderAttribute`. Omitted otherwise.

**End-user applications don't need to do anything.** This section only matters if you're publishing a library that wraps the Sync SDK.

If you are, add one of the following at assembly level (typically in `AssemblyInfo.cs` or a top-level `using` file). At request time the SDK walks the call stack, locates your assembly, reads the attribute, and composes the header value.

**1. Read name and version from the assembly (most common):**

```csharp
[assembly: SyncSourceTrackingHeaderAttribute]
```

Header becomes `<AssemblyName>;<AssemblyInformationalVersion>`.

**2. Override the name, keep version from the assembly:**

```csharp
[assembly: SyncSourceTrackingHeaderAttribute("Acme.Kontent.Ai.AwesomeTool")]
```

Useful when your NuGet package ID differs from your assembly name.

**3. Hard-code everything:**

```csharp
[assembly: SyncSourceTrackingHeaderAttribute("Acme.Kontent.Ai.AwesomeTool", 1, 2, 3, "beta")]
```

Useful when you want to pin the reported version independent of assembly metadata.

## Upgrade Guide

See `docs/upgrade-guide.md` for breaking changes and migration steps.

## Contributing

Contributions are welcome. Use [GitHub Issues](https://github.com/kontent-ai/sync-sdk-net/issues) for bug reports and feature requests, and open pull requests in this repository for code contributions.

## License

Licensed under the MIT License. See `LICENSE.md` for details.
