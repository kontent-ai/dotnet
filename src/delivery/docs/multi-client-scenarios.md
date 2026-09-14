# Multi-Client Scenarios Guide

This guide covers advanced scenarios where you need to work with multiple Kontent.ai environments, brands, or configurations within a single application.

## Table of Contents

- [Overview](#overview)
- [Use Cases](#use-cases)
- [Named Clients](#named-clients)
  - [Registration](#registration)
  - [Accessing Named Clients](#accessing-named-clients)
  - [Keyed Services (.NET 8+)](#keyed-services-net-8)
- [Client Factory](#client-factory)
  - [Basic Factory Usage](#basic-factory-usage)
- [Multi-Tenant Architectures](#multi-tenant-architectures)
  - [Fixed Tenants](#fixed-tenants)
  - [Dynamic Tenant Resolution](#dynamic-tenant-resolution)
- [Preview vs Production](#preview-vs-production)
  - [Enabling Preview](#enabling-preview)
  - [Separate Clients for Preview and Production](#separate-clients-for-preview-and-production)
  - [Selecting the Preview Client](#selecting-the-preview-client)
- [Environment-Specific Configuration](#environment-specific-configuration)
  - [Environment-Based Registration](#environment-based-registration)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)
  - [Client Not Found](#client-not-found)
  - [Wrong Environment ID](#wrong-environment-id)
  - [Cache Collisions](#cache-collisions)

## Overview

The SDK supports multiple simultaneous client instances, each with its own configuration. This enables:

- **Multi-tenant applications**: Serve content from different Kontent.ai environments
- **Multi-brand websites**: Manage multiple brands from a single application
- **Preview/production switching**: Dynamically select between preview and production APIs
- **Environment isolation**: Separate development, staging, and production configurations

## Use Cases

- **Multi-tenant SaaS** - one environment per customer.
- **Multi-brand** - one environment per brand, shared application.
- **Preview** - a preview client alongside the production one, chosen per request.
- **Aggregation** - reading from several environments in one response.

They are all the same mechanism: register clients by name, resolve the one you need.

## Named Clients

### Registration

Register multiple clients with unique names:

```csharp
using Kontent.Ai.Delivery;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Register first client
services.AddDeliveryClient("brand-a", delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "brand-a-environment-id";
}));

// Register second client
services.AddDeliveryClient("brand-b", delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "brand-b-environment-id";
}));

// Register third client with caching (requires Kontent.Ai.Delivery.Caching package)
services.AddDeliveryClient("brand-c", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "brand-c-environment-id";
    });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});

var serviceProvider = services.BuildServiceProvider();
```

### Accessing Named Clients

#### Using IDeliveryClientFactory

```csharp
public sealed class ContentService(IDeliveryClientFactory factory)
{
    public async Task<Homepage?> GetHomepageAsync(string brand, CancellationToken cancellationToken = default)
    {
        var result = await factory.Get(brand).GetItem<Homepage>("homepage").ExecuteAsync(cancellationToken);

        // Value is only meaningful on success - see the README's Error Handling section.
        return result.IsSuccess ? result.Value.Elements : null;
    }
}
```

### Keyed Services (.NET 8+)

Use .NET 8's keyed services for cleaner dependency injection:

```csharp
public class BrandAController : ControllerBase
{
    private readonly IDeliveryClient _client;

    // Inject specific client by key
    public BrandAController(
        [FromKeyedServices("brand-a")] IDeliveryClient client)
    {
        _client = client;
    }

    [HttpGet("homepage")]
    public async Task<IActionResult> GetHomepage()
    {
        var result = await _client.GetItem("homepage").ExecuteAsync();

        if (result.IsSuccess)
            return Ok(result.Value);

        return NotFound();
    }
}
```

## Client Factory

### Basic Factory Usage

```csharp
public interface IDeliveryClientFactory
{
    IDeliveryClient Get(string name);   // throws if not registered
    IDeliveryClient Get();              // returns the default client; throws if not registered
    IDeliveryClient? TryGet(string name); // returns null if not registered
}

// Usage
var factory = serviceProvider.GetRequiredService<IDeliveryClientFactory>();
var client = factory.Get("brand-a");
```

> [!NOTE]
> `Get(name)` throws when the name was never registered. Catch it at the boundary where the name comes from user input or a tenant lookup, rather than wrapping the factory.

## Multi-Tenant Architectures

### Fixed Tenants

When you have a known set of tenants:

```csharp
// appsettings.json
{
  "Tenants": [
    {
      "Name": "tenant1",
      "EnvironmentId": "guid-1",
      "CacheExpiration": "01:00:00"
    },
    {
      "Name": "tenant2",
      "EnvironmentId": "guid-2",
      "CacheExpiration": "02:00:00"
    }
  ]
}

// Startup configuration
var tenants = configuration.GetSection("Tenants").Get<List<TenantConfig>>();

foreach (var tenant in tenants)
{
    services.AddDeliveryClient(tenant.Name, delivery =>
    {
        delivery.Options.Configure(options =>
        {
            options.EnvironmentId = tenant.EnvironmentId;
        });
        delivery.UseMemoryCache(o => o.DefaultExpiration = tenant.CacheExpiration);
    });
}
```

### Dynamic Tenant Resolution

Resolve tenant from request context:

```csharp
public class TenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDeliveryClientFactory _factory;

    public TenantResolver(
        IHttpContextAccessor httpContextAccessor,
        IDeliveryClientFactory factory)
    {
        _httpContextAccessor = httpContextAccessor;
        _factory = factory;
    }

    public IDeliveryClient GetCurrentTenantClient()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        // Option 1: From subdomain
        var host = httpContext.Request.Host.Host;
        var subdomain = host.Split('.')[0];
        return _factory.Get(subdomain);

        // Option 2: From route
        var tenantId = httpContext.Request.RouteValues["tenantId"]?.ToString();
        return _factory.Get(tenantId);

        // Option 3: From header
        var tenantHeader = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        return _factory.Get(tenantHeader);

        // Option 4: From claims
        var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
        return _factory.Get(tenantClaim);
    }
}

// Registration
services.AddHttpContextAccessor();
services.AddScoped<TenantResolver>();

// Usage in controller
public class ContentController : ControllerBase
{
    private readonly TenantResolver _tenantResolver;

    public ContentController(TenantResolver tenantResolver)
    {
        _tenantResolver = tenantResolver;
    }

    [HttpGet("content/{codename}")]
    public async Task<IActionResult> GetContent(string codename)
    {
        var client = _tenantResolver.GetCurrentTenantClient();
        var result = await client.GetItem(codename).ExecuteAsync();

        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }
}
```

## Preview vs Production

### Enabling Preview

One client, reading unpublished content:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi = true;
    options.PreviewApiKey = "your-preview-api-key";
}));
```

Most applications want both, though - published content for visitors, preview for whoever is editing.

### Separate Clients for Preview and Production

```csharp
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
        options.UsePreviewApi = false;
    });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(2));
});

services.AddDeliveryClient("preview", delivery =>
{
    delivery.Options.Configure(options =>
    {
        options.EnvironmentId = "your-environment-id";
        options.UsePreviewApi = true;
        options.PreviewApiKey = "your-preview-api-key";
    });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromMinutes(5));
});
```

`UsePreviewApi = true` clients always bypass SDK cache reads/writes. Registering a cache manager for preview is optional and does not change that behavior.

### Selecting the Preview Client

Preview is a named client, so choosing it is one call. What matters is what comes *before* it: the
decision must be an authorization outcome, never something the caller can ask for.

```csharp
// Authorize first, then choose the client.
[Authorize(Policy = "CanPreviewContent")]
[HttpGet("preview/{codename}")]
public async Task<IActionResult> GetDraft(
    string codename,
    [FromServices] IDeliveryClientFactory factory,
    CancellationToken cancellationToken)
{
    var result = await factory.Get("preview").GetItem<Article>(codename).ExecuteAsync(cancellationToken);
    return result.IsSuccess ? Ok(result.Value) : NotFound();
}
```

Define `CanPreviewContent` with
[ASP.NET Core authorization](https://learn.microsoft.com/aspnet/core/security/authorization/policies) —
how you establish that identity is your application's concern, not the SDK's.

> [!WARNING]
> **Never derive preview from a query string, a cookie, or a shared URL secret.** Any of those can be supplied by the caller, which hands an unauthenticated visitor your server-held preview key and every unpublished item in the environment.

Two things that are easy to miss once the endpoint works:

- **Keep preview responses out of shared caches.** A `UsePreviewApi = true` client bypasses the *SDK's*
  cache, and that is all it does. ASP.NET Core output caching, a reverse proxy and a CDN are each
  unaware of it, so a preview response can still be stored and served to the next visitor. Mark the
  endpoint `[OutputCache(NoStore = true)]`, or keep it off any cached route.
- **Preview preference is not authorization.** An editor who *may* preview may still want to see the
  published site. Let the request carry that preference — a query flag is fine for this — but read it
  only after the policy has already granted access.

## Environment-Specific Configuration

### Environment-Based Registration

One client, configured differently per environment. Register it **without a name** — that is what makes
it the default, resolvable as a plain `IDeliveryClient` and by the factory's parameterless `Get()`:

```csharp
public static IServiceCollection AddKontentDelivery(
    this IServiceCollection services,
    IConfiguration configuration,
    IWebHostEnvironment environment) =>
    services.AddDeliveryClient(delivery =>
    {
        delivery.Options.BindConfiguration("DeliveryOptions");

        if (environment.IsDevelopment())
        {
            delivery.Options.Configure(options => options.UsePreviewApi(configuration["Kontent:PreviewApiKey"]!));
            delivery.UseMemoryCache(cache => cache.DefaultExpiration = TimeSpan.FromMinutes(5));
        }
        else
        {
            delivery.UseHybridCache(cache => cache.DefaultExpiration = TimeSpan.FromHours(4));
        }
    });
```

> [!IMPORTANT]
> `AddDeliveryClient("default", …)` does **not** register the default client — it registers an ordinary named client that happens to be called `default`. The unnamed overload is the only way to register the default, and client names are compared **ordinally**, so `Get("Production")` will not find a client registered as `"production"`.

## Best Practices

- **Name clients for what they are** - `production`, `preview`, a tenant id. The name is the lookup key and appears in the cache key prefix.
- **Register every client in one place**, so the set is readable at a glance and configuration binding stays uniform.
- **Give each client its own cache**, or none. A cache attaches per client; clients registered without one stay uncached.
- **Isolate cache identity per environment.** Two clients reading different environments must not share a cache namespace - see [Cache Key Prefixing](caching-guide.md#cache-key-prefixing).

## Troubleshooting

### Client Not Found

**Problem**: `InvalidOperationException: No client registered with name 'xyz'`

**Solution**: Verify client registration:

```csharp
// List all registered clients (for debugging)
var factory = serviceProvider.GetRequiredService<IDeliveryClientFactory>();

// Try to get client in try-catch
try
{
    var client = factory.Get("xyz");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Client 'xyz' not found. Registered clients: ...");
}
```

### Wrong Environment ID

**Problem**: Content from wrong environment is returned.

**Solution**: Log and verify environment IDs:

```csharp
services.AddDeliveryClient("brand-a", delivery => delivery.Options.Configure(options =>
{
    var envId = configuration["BrandA:EnvironmentId"];
    Console.WriteLine($"Registering brand-a with environment: {envId}");
    options.EnvironmentId = envId;
}));
```

### Cache Collisions

**Problem**: Cached content from one client appears for another.

**Solution**: Ensure each client uses its own cache namespace. The SDK does this for you: every key lives under the client's `DeliveryCacheOptions.KeyPrefix` (the client's name unless you set one) and its environment id, and the prefix covers FusionCache's own bookkeeping too, so two clients sharing a store cannot reach each other's entries, invalidations or purges. If you change `EnvironmentId` on an already-cached client at runtime, purge cache or recreate the client.

---

**Related Documentation**:
- [Main README](../README.md)
- [Caching Guide](caching-guide.md)
- [Performance Optimization Guide](performance-optimization.md)
