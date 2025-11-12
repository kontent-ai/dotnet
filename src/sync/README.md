> [!IMPORTANT]
> This SDK is currently in beta and may undergo breaking changes before being released into production.
> 
> For stable sync features, use the latest production release of delivery SDK (18.3.0), which includes them.
> From version 19 of delivery SDK (currently in beta) onwards, the sync features will only be available using this standalone SDK. 

# Kontent.ai Sync SDK for .NET

[![NuGet](https://img.shields.io/nuget/v/Kontent.Ai.Sync?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Sync)
[![License](https://img.shields.io/github/license/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/blob/master/LICENSE)
[![Contributors](https://img.shields.io/github/contributors/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/graphs/contributors)
[![Last Commit](https://img.shields.io/github/last-commit/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/commits/master)
[![Issues](https://img.shields.io/github/issues/kontent-ai/sync-sdk-net?style=for-the-badge)](https://github.com/kontent-ai/sync-sdk-net/issues)

A lightweight .NET SDK for the [Kontent.ai Sync API v2](https://kontent.ai/learn/docs/apis/openapi/sync-api-v2/), enabling efficient synchronization of content changes from your Kontent.ai projects.

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
    options.ApiMode = ApiMode.Preview;
    options.ApiKey = "your-preview-api-key";
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

## Sync Token Storage

The SDK returns a sync token with each response that must be stored and used for subsequent delta requests. **Token persistence is your responsibility** - the SDK does not store tokens.

Example implementations for different scenarios:

```csharp
// Example: In-memory storage (lost on restart)
private string? _syncToken;

private Task<string> LoadSyncTokenAsync()
{
    return Task.FromResult(_syncToken ?? string.Empty);
}

private Task SaveSyncTokenAsync(string syncToken)
{
    _syncToken = syncToken;
    return Task.CompletedTask;
}

// Example: File storage (simple persistence)
public class FileTokenStore
{
    private readonly string _filePath;

    public async Task<string> LoadSyncTokenAsync()
    {
        if (!File.Exists(_filePath)) return string.Empty;
        return await File.ReadAllTextAsync(_filePath);
    }

    public async Task SaveSyncTokenAsync(string syncToken)
    {
        await File.WriteAllTextAsync(_filePath, syncToken);
    }
}

// Example: Database storage (shared across instances)
public class DatabaseTokenStore
{
    private readonly DbContext _dbContext;

    public async Task<string> LoadSyncTokenAsync(string environmentId)
    {
        var token = await _dbContext.SyncTokens
            .Where(t => t.EnvironmentId == environmentId)
            .Select(t => t.Token)
            .FirstOrDefaultAsync();

        return token ?? string.Empty;
    }

    public async Task SaveSyncTokenAsync(string environmentId, string syncToken)
    {
        var existing = await _dbContext.SyncTokens
            .FirstOrDefaultAsync(t => t.EnvironmentId == environmentId);

        if (existing != null)
        {
            existing.Token = syncToken;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _dbContext.SyncTokens.Add(new SyncToken
            {
                EnvironmentId = environmentId,
                Token = syncToken,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }
}
```

**Important notes:**
- Choose storage based on your architecture (single instance vs. distributed, restart requirements, etc.)
- Losing a token means reinitializing sync from scratch
- Handle `SyncErrorReason.InvalidSyncToken` errors by reinitializing

## Configuration Options

### API Modes

The SDK supports three authentication modes:

```csharp
// Public Production API (no authentication)
services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.ApiMode = ApiMode.Public; // Default
});

// Preview API (requires API key)
services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.ApiMode = ApiMode.Preview;
    options.ApiKey = "your-preview-api-key";
});

// Secure Production API (requires API key)
services.AddSyncClient(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.ApiMode = ApiMode.Secure;
    options.ApiKey = "your-secure-access-api-key";
});
```

### Using Options Builder

```csharp
services.AddSyncClient(builder => builder
    .WithEnvironmentId("your-environment-id")
    .UsePreviewApi("your-preview-api-key")  // or .UseSecureApi() or .UseProductionApi()
    .DisableRetryPolicy()
    .Build());
```

### Using Configuration File

**appsettings.json:**
```json
{
  "SyncOptions": {
    "EnvironmentId": "your-environment-id",
    "ApiMode": "Preview",
    "ApiKey": "your-preview-api-key",
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
    options.ApiMode = ApiMode.Public;
});

services.AddSyncClient("staging", options =>
{
    options.EnvironmentId = "staging-environment-id";
    options.ApiMode = ApiMode.Preview;
    options.ApiKey = "staging-preview-key";
});

// ISyncClientFactory is automatically registered when you call AddSyncClient
// No additional registration is needed - just inject it
public class MultiTenantService
{
    private readonly ISyncClientFactory _syncClientFactory;

    public MultiTenantService(ISyncClientFactory syncClientFactory)
    {
        _syncClientFactory = syncClientFactory;
    }

    public async Task SyncProductionAsync()
    {
        var prodClient = _syncClientFactory.Get("production");
        var result = await prodClient.InitializeSyncAsync();
        // Process result...
    }

    public async Task SyncStagingAsync()
    {
        var stagingClient = _syncClientFactory.Get("staging");
        var result = await stagingClient.InitializeSyncAsync();
        // Process result...
    }
}
```

## Error Handling

The SDK uses the Result pattern for predictable error handling with structured error reasons:

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
    // Handle error with reason-based logic
    var error = result.Error;

    switch (error.Reason)
    {
        case SyncErrorReason.RateLimited:
            _logger.LogWarning("Rate limit exceeded, waiting before retry...");
            await Task.Delay(TimeSpan.FromSeconds(30));
            break;

        case SyncErrorReason.Unauthorized:
            _logger.LogError("Authentication failed. Check your API key.");
            break;

        case SyncErrorReason.NetworkError:
            _logger.LogError(error.InnerException, "Network connectivity issue");
            break;

        case SyncErrorReason.Timeout:
            _logger.LogWarning("Request timed out, retrying...");
            break;

        default:
            _logger.LogError(
                "Sync failed: {Message} (Reason: {Reason}, Status: {Status}, RequestId: {RequestId})",
                error.Message,
                error.Reason,
                result.StatusCode,
                error.RequestId);
            break;
    }
}
```

### Available Error Reasons

- `Unknown` - Unspecified error
- `InvalidResponse` - Invalid or unparsable API response
- `NotFound` - Resource not found (404)
- `Unauthorized` - Authentication failed (401/403)
- `RateLimited` - Too many requests (429)
- `Timeout` - Request timed out
- `NetworkError` - Connection or DNS failure
- `InvalidSyncToken` - Sync token is invalid or expired
- `InvalidConfiguration` - SDK configuration error
- `ServerError` - Internal server error (500+)

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

The Sync API returns a maximum of 100 items per entity type per response. The SDK provides multiple ways to handle pagination:

#### Automatic Pagination with GetAllDeltaAsync

The simplest approach - automatically fetches all available changes:

```csharp
public async Task SyncAllChangesAsync()
{
    var syncToken = await LoadSyncTokenAsync();

    // Automatically fetch all pages
    var result = await _syncClient.GetAllDeltaAsync(syncToken);

    if (result.IsSuccess)
    {
        _logger.LogInformation(
            "Fetched {Pages} pages with {Items} total items",
            result.PagesFetched,
            result.Responses.Sum(r => r.Items.Count));

        // Process all responses
        foreach (var response in result.Responses)
        {
            await ProcessDeltaAsync(response);
        }

        // Save the final sync token
        await SaveSyncTokenAsync(result.FinalSyncToken);
    }
}
```

#### Limit Pages to Control API Usage

```csharp
// Fetch maximum of 5 pages to control costs/time
var result = await _syncClient.GetAllDeltaAsync(syncToken, maxPages: 5);

if (result.WasLimitedByMaxPages)
{
    _logger.LogWarning("More changes available but stopped at page limit");
}
```

#### Manual Pagination with HasMoreChanges

For fine-grained control, use the `HasMoreChanges` property:

```csharp
public async Task SyncAllChangesAsync()
{
    var syncToken = await LoadSyncTokenAsync();

    do
    {
        var result = await _syncClient.GetDeltaAsync(syncToken);

        if (!result.IsSuccess)
        {
            // Handle error
            break;
        }

        await ProcessDeltaAsync(result.Value);

        // Update sync token
        syncToken = result.SyncToken;
        await SaveSyncTokenAsync(syncToken);

        // HasMoreChanges automatically checks if any collection has 100+ items
    } while (result.HasMoreChanges);
}
```

## Requirements

- **.NET 8.0** or later
- **Kontent.ai** environment with Sync API access

## Related SDKs

- **Delivery SDK**: [Kontent.Ai.Delivery](https://github.com/kontent-ai/delivery-sdk-net) - Content delivery
- **Management SDK**: [Kontent.Ai.Management](https://github.com/kontent-ai/management-sdk-net) - Content management

---

**Built with ❤️ for the Kontent.ai community**
