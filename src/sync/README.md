> [!IMPORTANT]
> This SDK is a work in progress. For existing sync API functionality in .NET, use delivery SDK v18.x.

# Kontent.ai Sync SDK for .NET

A modern, lightweight .NET SDK for the [Kontent.ai Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api/), enabling efficient synchronization of content changes from your Kontent.ai projects.

## Features

- ✅ **.NET 8.0** - Built with the latest .NET features
- ✅ **Simple API** - Direct methods, no complex query builders needed
- ✅ **Result Pattern** - Railway-oriented programming for robust error handling
- ✅ **Dependency Injection** - First-class DI support with multiple configuration styles
- ✅ **Resilience** - Built-in retry policies and timeout handling via Polly
- ✅ **Type-Safe** - Strongly-typed responses with full IntelliSense support
- ✅ **Modern C#** - Primary constructors, record types, nullable reference types

## Installation

```bash
dotnet add package Kontent.Ai.Sync
```

## Quick Start

### 1. Register the Sync Client

```csharp
using Kontent.Ai.Sync.Extensions;

// In your Program.cs or Startup.cs
services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi = true;
    options.PreviewApiKey = "your-preview-api-key";
});
```

### 2. Initialize Synchronization

```csharp
public class SyncService
{
    private readonly ISyncClient _syncClient;

    public SyncService(ISyncClient syncClient)
    {
        _syncClient = syncClient;
    }

    public async Task InitializeAsync()
    {
        var result = await _syncClient.InitializeSyncAsync();

        if (result.IsSuccess)
        {
            // Store the sync token for later use
            var syncToken = result.SyncToken;
            await SaveSyncTokenAsync(syncToken);

            Console.WriteLine($"Sync initialized. Token: {syncToken}");
        }
        else
        {
            Console.WriteLine($"Error: {result.Error?.Message}");
        }
    }
}
```

### 3. Retrieve Delta Updates

```csharp
public async Task SyncChangesAsync()
{
    var syncToken = await LoadSyncTokenAsync();
    var result = await _syncClient.GetDeltaAsync(syncToken);

    if (result.IsSuccess)
    {
        var delta = result.Value;

        // Process changed items (max 100 per response)
        foreach (var item in delta.Items)
        {
            Console.WriteLine($"Item {item.ChangeType}: {item.Data}");
        }

        // Process changed assets
        foreach (var asset in delta.Assets)
        {
            Console.WriteLine($"Asset {asset.ChangeType}: {asset.Data}");
        }

        // Process changed content types
        foreach (var type in delta.Types)
        {
            Console.WriteLine($"Type {type.ChangeType}: {type.Data}");
        }

        // Process changed languages
        foreach (var language in delta.Languages)
        {
            Console.WriteLine($"Language {language.ChangeType}: {language.Data}");
        }

        // Process changed taxonomies
        foreach (var taxonomy in delta.Taxonomies)
        {
            Console.WriteLine($"Taxonomy {taxonomy.ChangeType}: {taxonomy.Data}");
        }

        // Save the new sync token for next sync
        await SaveSyncTokenAsync(result.SyncToken);
    }
}
```

## Configuration Options

### Using Options Builder

```csharp
services.AddSyncClient(builder => builder
    .WithEnvironmentId("your-environment-id")
    .UsePreviewApi("your-preview-api-key")
    .DisableRetryPolicy()
    .Build());
```

### Using Configuration File

**appsettings.json:**
```json
{
  "SyncOptions": {
    "EnvironmentId": "your-environment-id",
    "UsePreviewApi": true,
    "PreviewApiKey": "your-preview-api-key",
    "EnableResilience": true
  }
}
```

**Program.cs:**
```csharp
services.AddSyncClient(configuration, "SyncOptions");
```

### Named Clients

Register multiple clients for different environments:

```csharp
services.AddSyncClient("production", options =>
{
    options.EnvironmentId = "prod-environment-id";
});

services.AddSyncClient("staging", options =>
{
    options.EnvironmentId = "staging-environment-id";
    options.UsePreviewApi = true;
    options.PreviewApiKey = "staging-preview-key";
});

// Access via factory (requires ISyncClientFactory - to be implemented)
var prodClient = syncClientFactory.Get("production");
var stagingClient = syncClientFactory.Get("staging");
```

## Error Handling

The SDK uses the Result pattern for predictable error handling:

```csharp
var result = await _syncClient.GetDeltaAsync(syncToken);

if (result.IsSuccess)
{
    // Process successful response
    var delta = result.Value;
    ProcessDelta(delta);
}
else
{
    // Handle error
    var error = result.Error;
    _logger.LogError(
        "Sync failed: {Message} (Status: {Status}, RequestId: {RequestId})",
        error.Message,
        result.StatusCode,
        error.RequestId);
}
```

## Resilience & Retry Policies

The SDK includes built-in resilience policies using Polly:

- **Retry**: 3 attempts with exponential backoff
- **Timeout**: 30 seconds per request
- **Retryable Status Codes**: 429, 408, 500, 502, 503, 504

### Custom Resilience Configuration

```csharp
services.AddSyncClient(
    options => { /* ... */ },
    configureHttpClient: null,
    configureResilience: builder =>
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromSeconds(2)
        });
    });
```

### Disable Resilience

```csharp
services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.EnableResilience = false; // Disable retry/timeout policies
});
```

## Advanced Usage

### Custom HTTP Client Configuration

```csharp
services.AddSyncClient(
    options => { /* ... */ },
    configureHttpClient: httpClient =>
    {
        httpClient.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("http://proxy:8080")
        });
    });
```

### Pagination

The Sync API returns a maximum of 100 items per entity type. Use the sync token to paginate:

```csharp
public async Task SyncAllChangesAsync()
{
    var syncToken = await LoadSyncTokenAsync();
    bool hasMoreChanges = true;

    while (hasMoreChanges)
    {
        var result = await _syncClient.GetDeltaAsync(syncToken);

        if (!result.IsSuccess)
        {
            // Handle error
            break;
        }

        var delta = result.Value;
        await ProcessDeltaAsync(delta);

        // Check if there are more changes
        hasMoreChanges = delta.Items.Count == 100 ||
                         delta.Assets.Count == 100 ||
                         delta.Types.Count == 100 ||
                         delta.Languages.Count == 100 ||
                         delta.Taxonomies.Count == 100;

        // Update sync token
        syncToken = result.SyncToken;
        await SaveSyncTokenAsync(syncToken);
    }
}
```

## Architecture

The SDK follows the proven architectural patterns from the Kontent.ai Delivery SDK:

- **Abstractions Project**: Public API contracts and interfaces
- **Implementation Project**: Internal implementations using Refit
- **Result Pattern**: Functional error handling without exceptions
- **Options Pattern**: ASP.NET Core configuration integration
- **Modern .NET**: C# 12, primary constructors, record types

## Requirements

- **.NET 8.0** or later
- **Kontent.ai** environment with Sync API access

## License

This SDK is provided as-is for use with Kontent.ai services.

## Support

For issues, feature requests, or questions:
- GitHub Issues: [kontent-ai/delivery-sdk-net](https://github.com/kontent-ai/delivery-sdk-net)
- Kontent.ai Documentation: [Sync API Reference](https://kontent.ai/learn/docs/apis/openapi/sync-api/)

## Related SDKs

- **Delivery SDK**: [Kontent.Ai.Delivery](https://github.com/kontent-ai/delivery-sdk-net) - Content delivery
- **Management SDK**: [Kontent.Ai.Management](https://github.com/kontent-ai/management-sdk-net) - Content management

---

**Built with ❤️ for the Kontent.ai community**
