# Kontent.ai Delivery SDK for .NET

[![NuGet](https://img.shields.io/nuget/v/Kontent.Ai.Delivery?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Delivery)
[![Downloads](https://img.shields.io/nuget/dt/Kontent.Ai.Delivery?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Delivery)

The official .NET SDK for the [Kontent.ai Delivery API](https://kontent.ai/learn/docs/apis/openapi/delivery-api/), enabling you to retrieve content from your Kontent.ai projects with a modern, type-safe, and highly extensible client library.

This README documents **20.x**, which targets `net10.0`. Coming from 19.x or earlier? See [Upgrade Guide](#upgrade-guide) — registration and caching both moved onto a builder.

> [!TIP]
> **Building an ASP.NET Core app?** Check out [**Kontent.ai ASP.NET Core Extensions**](https://github.com/kontent-ai/dotnet/tree/main/src/aspnetcore) — a companion package that adds a `<rich-text>` tag helper for rendering Kontent.ai rich text in Razor views (with full `IHtmlResolver` integration), an `<img-asset>` tag helper for responsive images with automatic `srcset`/`sizes`, webhook signature validation middleware, and cache invalidation straight from a webhook notification.

## Table of Contents

- [Installation](#installation)
- [Upgrade Guide](#upgrade-guide)
- [Quick Start](#quick-start)
- [Setting Up the Delivery Client](#setting-up-the-delivery-client)
- [Caching](#caching)
- [Rich Text](#rich-text)
- [Configuration Options](#configuration-options)
- [Error Handling](#error-handling)
- [Source Tracking (for Tool Authors)](#source-tracking-for-tool-authors)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)

## Installation

Install the SDK via NuGet Package Manager:

```bash
dotnet add package Kontent.Ai.Delivery
```

Or via the Package Manager Console:

```powershell
Install-Package Kontent.Ai.Delivery
```

**Optional packages:**

| Package | Purpose |
|---------|---------|
| `Kontent.Ai.Delivery.Caching` | FusionCache-backed memory and hybrid caching |
| `Kontent.Ai.Delivery.SourceGeneration` | Compile-time type provider via source generation |

```bash
dotnet add package Kontent.Ai.Delivery.Caching
dotnet add package Kontent.Ai.Delivery.SourceGeneration
```

All of them ship on one version. See the [changelog](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/CHANGELOG.md) for what each release changed.

## Upgrade Guide

Upgrade guides are kept one per major under [`docs/upgrade/`](https://github.com/kontent-ai/dotnet/tree/main/src/delivery/docs/upgrade); skipping a major means reading them in sequence.

- Coming from **19.x** — read [19 → 20](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade/19-to-20.md). The move to .NET 10 and the builder registration are the work; two behaviour changes compile unchanged and are listed first.
- Coming from **18.x** — read [18 → 19](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade/18-to-19.md) first, then 19 → 20.

## Quick Start

Here's a minimal example to get you started:

```csharp
using Kontent.Ai.Delivery;
using Microsoft.Extensions.DependencyInjection;

// Set up dependency injection
var services = new ServiceCollection();

services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
}));

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<IDeliveryClient>();

// Retrieve content
var result = await client.GetItem("homepage").ExecuteAsync();

if (result.IsSuccess)
{
    var item = result.Value;
    Console.WriteLine($"Title: {item.System.Name}");
}
```

## Setting Up the Delivery Client

The SDK is designed to work with .NET's dependency injection container. Register the `IDeliveryClient` in your `Program.cs` or `Startup.cs`:

### Basic Registration

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
}));
```

### Registration from Configuration

```csharp
// appsettings.json
{
  "DeliveryOptions": {
    "EnvironmentId": "your-environment-id",
    "UsePreviewApi": false
  }
}

// Program.cs
services.AddDeliveryClient(delivery => delivery.Options.BindConfiguration("DeliveryOptions"));        // in a host, from the container's IConfiguration
services.AddDeliveryClient(delivery => delivery.Options.Bind(configuration.GetSection("MyDeliverySection"))); // or a section you hold
```

The default section name is available as `DeliveryOptions.DefaultConfigurationSectionName`, so tooling
that resolves the SDK's configuration from the same sources does not have to hard-code it.

`Options` is the client's `OptionsBuilder<DeliveryOptions>`, so everything the options system offers is
there: `Configure`, `Configure<TDependency>`, `Bind`, `BindConfiguration`, `PostConfigure`, `Validate`.
Binding is change-token backed: edits to the underlying source are picked up through
`IOptionsMonitor<DeliveryOptions>` without rebuilding the container. Binding from configuration and
customizing the pipeline are two steps on the same builder:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Options.BindConfiguration("DeliveryOptions");
    delivery.ConfigureResilience(pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 5 }));
});
```

### Registration from Other DI Services

Use `Options.Configure<IServiceProvider>` when Delivery options need values from other registered services:

```csharp
services.Configure<SiteOptions>(configuration.GetSection("Site"));

services.AddDeliveryClient(delivery => delivery.Options.Configure<IServiceProvider>((options, sp) =>
{
    var site = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
    options.EnvironmentId = site.EnvironmentId;
}));
```

The callback must not resolve `IDeliveryClient`, `IOptions<DeliveryOptions>` or anything that depends on
them: doing so re-enters the options factory, and the container recurses without bound.

### API Mode Helpers

The members that set more than one property come as extension methods on `DeliveryOptions`, usable inside
`Configure` and on any instance:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi("your-preview-api-key");   // or UseProductionApi(), UseProductionApi(secureAccessApiKey), UseCustomEndpoint(url)
}));
```

### Source-Generated Type Provider (Recommended)

Nothing to register. When your models carry the `[ContentTypeCodename]` attribute and the models project
references `Kontent.Ai.Delivery.SourceGeneration`, the generator emits a `GeneratedTypeProvider` at compile
time and the SDK discovers it at runtime, searching the entry assembly and its references:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
}));
```

Keep the attributed models in a *single* project for auto-discovery to be predictable. If they are split
across compilations on purpose, register an `ITypeProvider` explicitly instead. The [Content Models
guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/models.md#source-generation-for-type-resolution)
covers what the generator produces and the diagnostics it reports.

> [!NOTE]
> A model must be a class or a record class. The SDK hydrates elements on the instance it deserialized, so a struct would be copied and its values lost — the generator reports `KDSG003`, and the client throws `NotSupportedException` if a struct reaches it another way.

### Registering a Custom Type Provider

To override the auto-discovered provider, register your own — before the client, or on the builder's
`Services`. The SDK registers its default with `TryAddSingleton`, so yours wins either way:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Services.AddSingleton<ITypeProvider, MyCustomTypeProvider>();
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
});
```

### Without Dependency Injection

For console applications, scripts, or anywhere a container is not available, `DeliveryClient.Create` takes
the same builder as `AddDeliveryClient` and runs the same registration inside a private container the
client owns:

```csharp
await using var client = DeliveryClient.Create(delivery => delivery.Options.Configure(o => o.EnvironmentId = "your-environment-id"));

// Anything the container path can do, this can do - caching, resilience, an explicit type provider.
await using var cachedClient = DeliveryClient.Create(delivery =>
{
    delivery.Options.Configure(o => o.EnvironmentId = "your-environment-id");
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromMinutes(30));
});

// Or from a pre-built options instance.
await using var fromOptions = DeliveryClient.Create(new DeliveryOptions { EnvironmentId = "your-environment-id" });
```

The builder is one type in both hosting modes:

- `.Options` — the client's `OptionsBuilder<DeliveryOptions>` (`Configure`, `Bind`, `BindConfiguration`, …)
- `.HttpClient` — the named `IHttpClientBuilder` the transport is built on (`ConfigurePrimaryHttpMessageHandler`, `AddHttpMessageHandler`, …)
- `.ConfigureResilience(...)` — replaces the default resilience pipeline
- `.Services` and `.Name` — what everything else attaches to: a custom `ITypeProvider`, an `ILoggerFactory`, the caching package's `UseMemoryCache` / `UseHybridCache` / `UseCacheManager`

`Create` returns the concrete `DeliveryClient`, which owns the container it was built from — disposing it
tears that down, which is why the examples use `await using`. Keep the result as `var` (or
`DeliveryClient`); widening it to `IDeliveryClient` drops the disposal, because the interface deliberately
does not carry it. A client resolved from a container is owned by that container, so there is nothing for
you to dispose there. `IDeliveryClientFactory` is the other half of that split: it resolves named clients
from a container you own, while `Create` builds a standalone one over a container it owns. Invalid options
surface as `OptionsValidationException` from `Create`.

> [!NOTE]
> `UseCustomEndpoint(...)` sets the same endpoint for both Production and Preview. Real deployments usually differ, so if you need both modes on custom domains, register separate named clients and give each its own endpoint.

## Caching

Caching is the first thing to add to a production application - it is also the SDK's answer to rate
limits. It ships separately:

```bash
dotnet add package Kontent.Ai.Delivery.Caching
```

Attach it to a client in the same callback that configures it:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});
```

`UseHybridCache` is the L1+L2 form for multi-instance deployments, backed by any `IDistributedCache`.

> [!IMPORTANT]
> **Running more than one instance? Register an `IFusionCacheBackplane` as well.** Part of the invalidation state lives in each instance rather than in the shared cache, so without one an instance can go on serving content another already evicted. The SDK picks the backplane up from the container automatically.

Caching is transparent once attached: cacheable queries are cached, keyed by their parameters. Dynamic
queries, preview clients and `WaitForLoadingNewContent(true)` are deliberately never cached.

The **[Caching Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md)** has the rest - hybrid setup, cache keys, expiration,
dependency keys for output caching, webhook invalidation, purging, and multi-tenant scenarios.

## Rich Text

A rich text element is structured content, not an HTML string. Render it with `ToHtmlAsync()`:

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();
var html = await result.Value.Elements.BodyCopy.ToHtmlAsync();
```

The default resolver handles text, images and standard HTML. To control how content item links and
embedded content render, build a resolver and pass it in:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver("article", async (link, resolveChildren) =>
        $"<a href=\"/articles/{link.Metadata?.UrlSlug}\">{await resolveChildren(link.Children)}</a>")
    .WithContentResolver<Tweet>(t => $"<blockquote>{t.Elements.TweetText}</blockquote>")
    .Build();

var html = await result.Value.Elements.BodyCopy.ToHtmlAsync(resolver);
```

Rich text is also enumerable, when you want the blocks rather than the HTML.

The **[Rich Text Customization Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/rich-text-customization.md)** covers the rest: registering
the resolver in DI, async and nested resolution, inline images, custom HTML nodes, and the extension
methods for reading blocks out of an element.

> [!TIP]
> Rendering in Razor? [`Kontent.Ai.AspNetCore`](https://github.com/kontent-ai/dotnet/tree/main/src/aspnetcore) has a `<rich-text>` tag helper that picks the registered `IHtmlResolver` up for you.

## Configuration Options

The `DeliveryOptions` class provides comprehensive configuration:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    // Required: Your Kontent.ai environment ID
    options.EnvironmentId = "your-environment-id";

    // Preview API settings
    options.UsePreviewApi = false;
    options.PreviewApiKey = "your-preview-api-key";

    // Secured production API (if enabled in Kontent.ai)
    options.UseSecureAccess = false;
    options.SecureAccessApiKey = "your-secure-api-key";

    // Retry and resilience settings
    options.EnableResilience = true;

    // Default image rendition preset
    options.DefaultRenditionPreset = "default";

    // Custom asset domain (rewrites all asset URLs to use your CDN)
    options.CustomAssetDomain = "https://assets.example.com";

    // Custom endpoints (for proxy scenarios, set independently)
    options.ProductionEndpoint = "https://deliver.kontent.ai";
    options.PreviewEndpoint = "https://preview-deliver.kontent.ai";
}));
```

`options.UseCustomEndpoint(...)` is a convenience method that sets both endpoints to the same URL. For distinct preview/production custom domains, configure separate clients and set endpoints per client.

The HTTP client and the resilience pipeline are configured on the same builder. `HttpClient` is the
named `IHttpClientBuilder` the transport is built on, so every `Microsoft.Extensions.Http` extension
applies, and whatever you configure there runs after the SDK's own setup. Leave `HttpClient.Timeout`
alone: the SDK sets it as the ceiling on the whole call, and `DeliveryOptions.Timeout` is the way to
change that ceiling, so an override there silently caps the retry sequence:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.HttpClient.ConfigureHttpClient(client => client.DefaultRequestHeaders.Add("X-App", "my-app"));
    delivery.ConfigureResilience(pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 5,
        Delay = TimeSpan.FromSeconds(2)
    }));
});
```

### Timeouts

Two clocks bound a request, and they are not the same one:

- **Per attempt** — the default resilience pipeline cancels any single HTTP attempt after 30 seconds and
  retries it on a fresh connection. Up to four attempts, each with its own budget.
- **The whole call** — `DeliveryOptions.Timeout` covers every attempt *and* the waits between them.

`Timeout` is unset by default, which keeps the SDK's own rule: the default pipeline bounds each attempt,
so the call runs as long as its retries need; with `EnableResilience = false` or a pipeline of your own,
`HttpClient`'s 100-second default applies, because nothing else is known to bound the request.

Set it and it always wins, whatever the pipeline:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.Timeout = TimeSpan.FromMinutes(5);
}));
```

`Timeout.InfiniteTimeSpan` removes the ceiling outright. Note that it outranks `Retry-After`: when the API
rate-limits you, the pipeline waits exactly as long as the server asked, but the call is still cut short if
your ceiling runs out first.

## Error Handling

The SDK uses a result pattern instead of throwing exceptions for API errors. This makes error handling explicit and predictable.

### Checking for Errors

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess)
{
    var article = result.Value;
    // Process article
}
else
{
    // Handle error
    var error = result.Error;
    Console.WriteLine($"Error: {error.Message}");
    Console.WriteLine($"Status: {result.StatusCode}");

    // Error details for debugging/logging
    if (error.RequestId != null)
        Console.WriteLine($"Request ID: {error.RequestId}");

    if (error.ErrorCode.HasValue)
        Console.WriteLine($"Error Code: {error.ErrorCode}");
}
```

### IError Properties

| Property | Description |
|----------|-------------|
| `Message` | Human-readable error description |
| `RequestId` | Unique request ID for Kontent.ai support |
| `ErrorCode` | Kontent.ai-specific error code |
| `SpecificCode` | More specific error code |
| `Exception` | Underlying exception (for network errors, etc.) |

## Source Tracking (for Tool Authors)

Every request the SDK sends carries two analytics headers:

- **`X-KC-SDKID`** — identifies this SDK. Always `nuget.org;Kontent.Ai.Delivery;<version>`. Not configurable.
- **`X-KC-SOURCE`** — identifies a library built *on top of* the SDK. Set only when a caller assembly opts in. Omitted otherwise.

**End-user applications need do nothing here.** This matters only if you publish a library that wraps the Delivery SDK. If you do, add one of these at assembly level (`AssemblyInfo.cs`, or a top-level file); at request time the SDK walks the call stack, finds your assembly and reads the attribute:

```csharp
// Name and version from the assembly — the usual case.
[assembly: DeliverySourceTrackingHeader]

// Override the name (your package id differs from your assembly name), version still from the assembly.
[assembly: DeliverySourceTrackingHeader("Acme.Kontent.Ai.AwesomeTool")]

// Pin both, independent of assembly metadata.
[assembly: DeliverySourceTrackingHeader("Acme.Kontent.Ai.AwesomeTool", 1, 2, 3, "beta")]
```

## Documentation

This README covers installation, registration and configuration. Everything else lives beside it:

| Guide | What it answers |
|---|---|
| **[Querying](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/queries.md)** | Items, types, taxonomies, used-in lookups, filtering, ordering, projection, paging, languages, and what a result carries |
| **[Content Models](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/models.md)** | Generated records, source-generated type resolution, linked items, dynamic access when the type is unknown until runtime |
| **[Caching](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md)** | Memory and hybrid caches, keys, expiration, dependency keys, webhook invalidation, purging, multi-tenancy |
| **[Rich Text](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/rich-text-customization.md)** | Link and embedded-content resolvers, inline images, custom HTML nodes, reading blocks |
| **[Assets and Images](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/assets-and-images.md)** | Renditions, a custom asset domain, and `ImageUrlBuilder` transformations |
| **[Multiple Clients](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/multi-client-scenarios.md)** | Named clients, preview vs production, multi-tenant and multi-brand setups |
| **[Performance](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/performance-optimization.md)** | Query shaping, rate limits, parallelism, monitoring |
| **[Extensibility](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/extensibility-guide.md)** | Custom type providers and property mappers |
| **[Architecture](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/for-developers.md)** | How the SDK is put together, one invariant per boundary - start here to contribute |
| **[Upgrade Guides](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade)** | One per major: [18 &rarr; 19](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade/18-to-19.md), [19 &rarr; 20](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade/19-to-20.md) |

## Contributing

Contributions are welcome. Use [GitHub Issues](https://github.com/kontent-ai/dotnet/issues) for bug reports and feature requests, and open pull requests in this repository for code contributions.

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md) file for details.

---

**Questions or feedback?** Visit our [GitHub Issues](https://github.com/kontent-ai/dotnet/issues) or check the [Kontent.ai Developer Hub](https://kontent.ai/learn/docs).
