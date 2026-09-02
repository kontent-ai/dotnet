# Kontent.ai Sync SDK for .NET

[![NuGet](https://img.shields.io/nuget/v/Kontent.Ai.Sync?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Sync)
[![Downloads](https://img.shields.io/nuget/dt/Kontent.Ai.Sync?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Sync)

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

services.AddSyncClient(sync => sync.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi("your-preview-api-key");
}));
```

`AddSyncClient` hands you a builder for the one client being registered. `Options` is its
`OptionsBuilder<SyncOptions>`, so everything the options pattern offers - `Configure`, `Bind`,
`BindConfiguration`, `Validate` - is available without the SDK wrapping it; the rest of this README
shows the pieces as they come up.

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
    Console.WriteLine($"{item.Timestamp:u}  {item.ChangeType}  {item.Data.System.Codename}");
}

await SaveSyncTokenAsync(deltaResult.SyncToken);
```

### 4. Walk every page

`EnumerateDeltaAsync` keeps requesting until the API reports an empty response, which is how it says
you have caught up. Requests are made as you iterate, so bound the walk with `Take` or by breaking out
of the loop — nothing is fetched ahead of you.

```csharp
var token = syncToken;

await foreach (var page in syncClient.EnumerateDeltaAsync(syncToken, cancellationToken))
{
    if (!page.IsSuccess)
    {
        Console.WriteLine($"Sync failed: {page.Error?.Message}");
        break;
    }

    foreach (var item in page.Value.Items)
    {
        Console.WriteLine($"{item.Timestamp:u}  {item.ChangeType}  {item.Data.System.Codename}");
    }

    token = page.SyncToken;
}

// An empty sequence means there was nothing new, and the token you passed in is still current.
await SaveSyncTokenAsync(token);
```

## What a delta page contains

Each of the four collections holds `SyncChange<TData>` entries sharing one envelope — what changed,
when, and the metadata:

| Member | |
|---|---|
| `ChangeType` | `Changed` or `Deleted` |
| `Timestamp` | when the change occurred in the Delivery API, UTC |
| `Data` | the entity's metadata, on every change — a deletion names what was deleted |

The payload differs per collection, because the API's does:

| Collection | `Data` | `Data.System` |
|---|---|---|
| `Items` | `SyncItemData` | id, collection, name, codename, language, type, last modified, workflow, workflow step |
| `Types` | `SyncTypeData` | id, name, codename, last modified |
| `Taxonomies` | `SyncTaxonomyData` | id, name, codename, last modified |
| `Languages` | `SyncLanguageData` | id, name, codename |

`Workflow` and `WorkflowStep` are absent for components. A language carries no last-modified stamp,
which is why the four payloads are separate types rather than one.

## Configuration

The builder passed to `AddSyncClient` (and to `SyncClient.Create`, below) has four members and one method:

| Member | What it is |
|---|---|
| `Options` | The client's `OptionsBuilder<SyncOptions>` - `Configure`, `Configure<TDependency>`, `Bind`, `BindConfiguration`, `Validate`, `PostConfigure` |
| `HttpClient` | The `IHttpClientBuilder` the transport is built on - every `Microsoft.Extensions.Http` extension applies |
| `ConfigureResilience(...)` | Replaces the default resilience pipeline |
| `Services`, `Name` | The service collection and the client's registration key, for anything you attach to this client yourself |

One step fits an expression lambda; several go in a statement lambda. Whatever you chain runs after
the SDK's own setup, so it wins.

### API modes

`SyncOptions.ApiMode` and `ApiKey` decide the API; the extension methods set the two together.

```csharp
// Public Production API
services.AddSyncClient(sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.UseProductionApi();
}));

// Preview API
services.AddSyncClient(sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.UsePreviewApi("preview-api-key");
}));

// Secure Production API - a Delivery API key with secure access enabled
services.AddSyncClient(sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.UseProductionApi("secure-access-api-key");
}));
```

`UseCustomEndpoint(...)` points both API modes at one endpoint. Single settings are plain properties:
`EnableResilience`, `Timeout`, `ProductionEndpoint`, `PreviewEndpoint`.

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

Bind the section, or - in a host, where `IConfiguration` is in the container - name it:

```csharp
services.AddSyncClient(sync => sync.Options.Bind(configuration.GetSection("SyncOptions")));
services.AddSyncClient(sync => sync.Options.BindConfiguration("MySyncSection"));
```

The default section name is available as `SyncOptions.DefaultConfigurationSectionName`, so tooling that
resolves the SDK's configuration from the same sources does not have to hard-code it.

Binding this way is change-token backed: edits to the underlying source are picked up through
`IOptionsMonitor<SyncOptions>` without rebuilding the container. Binding and the other steps combine
freely:

```csharp
services.AddSyncClient(sync =>
{
    sync.Options.BindConfiguration("SyncOptions");
    sync.ConfigureResilience(pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 5 }));
});
```

A pre-built instance works too; its values are copied onto the options the container materializes, and
the object itself is not registered:

```csharp
services.AddSyncClient(new SyncOptions { EnvironmentId = "your-environment-id" }.UsePreviewApi("preview-api-key"));
```

### Timeouts

Two clocks bound a request, and they are not the same one:

- **Per attempt** — the default resilience pipeline cancels any single HTTP attempt after 30 seconds and
  retries it on a fresh connection. Up to four attempts, each with its own budget.
- **The whole call** — `SyncOptions.Timeout` covers every attempt *and* the waits between them.

`Timeout` is unset by default, which keeps the SDK's own rule: the default pipeline bounds each attempt,
so the call runs as long as its retries need; with `EnableResilience = false` or a pipeline of your own,
`HttpClient`'s 100-second default applies, because nothing else is known to bound the request.

Set it and it always wins, whatever the pipeline:

```csharp
services.AddSyncClient(sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.Timeout = TimeSpan.FromMinutes(5);
}));
```

`Timeout.InfiniteTimeSpan` removes the ceiling outright. Note that it outranks `Retry-After`: when the API
rate-limits you, the pipeline waits exactly as long as the server asked, but the call is still cut short if
your ceiling runs out first.

### Options from other registered services

When the options depend on something else in the container — a secret store, a tenant resolver — use
`Configure<TDependency>` on the options builder:

```csharp
services.AddSyncClient(sync => sync.Options.Configure<ISecretStore>((options, secrets) =>
{
    options.EnvironmentId = secrets.EnvironmentId;
    options.ApiKey = secrets.SyncApiKey;
}));
```

### The HTTP client

`HttpClient` is the named `IHttpClientBuilder` the SDK registered, after the SDK's own handlers are on
it, so a handler or primary handler you add runs on top of them:

```csharp
services.AddSyncClient(sync =>
{
    sync.Options.Configure(o => o.EnvironmentId = "your-environment-id");
    sync.HttpClient.AddHttpMessageHandler<MyAuditingHandler>();
});
```

## Standalone client (without DI)

For console apps, Azure Functions isolated workers, scripts, or tests where a container of your own is not
available, `SyncClient.Create` takes the same builder and runs the same registration inside a private
container the built client owns, so the client must be disposed.

```csharp
await using var client = SyncClient.Create(sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.UsePreviewApi("preview-api-key");
}));

var result = await client.InitializeSyncAsync();
```

Logging, resilience and anything else the container path can do are the same calls:

```csharp
await using var client = SyncClient.Create(sync =>
{
    sync.Services.AddSingleton(loggerFactory);
    sync.Options.Configure(o => o.EnvironmentId = "env-id");
    sync.ConfigureResilience(pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 5 }));
});
```

`SyncOptions.Timeout` applies here too, and matters with a pipeline of your own:

```csharp
await using var client = SyncClient.Create(sync =>
{
    sync.Options.Configure(o =>
    {
        o.EnvironmentId = "your-environment-id";
        o.Timeout = TimeSpan.FromMinutes(5);
    });
    sync.ConfigureResilience(pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(2)));
});
```

Without the `Timeout` line, supplying your own pipeline leaves `HttpClient`'s 100-second default in
charge, which would cut the two-minute attempt short.

The returned client is thread-safe and should be used as a singleton for the lifetime of your
application. Each `Create` call builds an independent client that owns the `HttpClient` it drew and the
private container behind it, which is why it is disposable — dispose it and no further request goes out;
the pooled connections close once the HTTP client factory releases the handler. A client resolved from
your own container is owned by that container instead, so there is nothing for you to dispose there.
`ISyncClientFactory`, below, is the other thing with a similar name: it resolves a *named* client from
your container, while `Create` builds a standalone one.

## Named Clients

```csharp
services.AddSyncClient("production", sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "prod-environment-id";
    o.UseProductionApi();
}));

services.AddSyncClient("preview", sync => sync.Options.Configure(o =>
{
    o.EnvironmentId = "preview-environment-id";
    o.UsePreviewApi("preview-api-key");
}));

public sealed class MultiEnvironmentService(ISyncClientFactory factory)
{
    public ISyncClient ProductionClient => factory.Get("production");
    public ISyncClient PreviewClient => factory.Get("preview");
}
```

The name is the only difference, so named clients bind from configuration the same way:

```csharp
services.AddSyncClient("production", sync => sync.Options.BindConfiguration("Sync:Production"));
services.AddSyncClient("preview", sync => sync.Options.Bind(configuration.GetSection("Sync:Preview")));
```

## Error Handling

Every call returns a result rather than throwing. `ISyncResult` carries the outcome — success, error,
status, the continuation token — and `ISyncResult<T>` adds `Value` for the calls that return content.
`InitializeSyncAsync` returns the non-generic form, because initialization produces a token rather than
content; `GetDeltaAsync` and `EnumerateDeltaAsync` return the generic one.

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
- `ISyncResult.StatusCode` (`HttpStatusCode`)
- `ISyncResult.ResponseHeaders`
- `ISyncResult.RequestUrl`
- `ISyncResult.SyncToken`
- `ISyncResult<T>.Value` — the delta payload, on the calls that return content
- `IError.Message`
- `IError.RequestId`
- `IError.ErrorCode` / `IError.SpecificCode`
- `IError.Exception`

## Token Persistence

The SDK does not persist sync tokens. Store `SyncToken` after every successful call and pass it into
the next `GetDeltaAsync` or `EnumerateDeltaAsync` call. Every successful response carries one, so it is
never null on a successful result; a response without it fails rather than returning a result you could
not continue from.

Where you store it during a walk is a choice. Saving once after the loop means a crash part-way through
reprocesses from the previous token — some changes arrive twice, none are missed. Saving after each page
resumes closer to where you stopped. Saving *before* processing a page is the one variant that can lose
work.

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

- Coming from **1.0** — see the [1.0 → 2.0 upgrade guide](https://github.com/kontent-ai/dotnet/blob/main/src/sync/docs/upgrade-guide-1.0-to-2.0.md).
  The two changes that need real work are the .NET 10 move and paging, which is now a stream you enumerate.
- Coming from the **sync methods that used to live in `Kontent.Ai.Delivery`** — those were removed in
  Delivery 19.0. Move to `Kontent.Ai.Sync` by following its [Quick Start](#quick-start): sync has its own
  client, and every call returns `ISyncResult<T>` rather than throwing.

## Contributing

Contributions are welcome. Use [GitHub Issues](https://github.com/kontent-ai/dotnet/issues) for bug reports and feature requests, and open pull requests in this repository for code contributions.

## License

Licensed under the MIT License. See `LICENSE.md` for details.
