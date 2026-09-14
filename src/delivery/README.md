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
- [Basic Usage](#basic-usage)
  - [Setting Up the Delivery Client](#setting-up-the-delivery-client)
  - [Retrieving Content](#retrieving-content)
  - [Content Types and Elements](#content-types-and-elements)
  - [Taxonomies](#taxonomies)
  - [Reference Lookups (Used In)](#reference-lookups-used-in)
  - [Filtering and Querying](#filtering-and-querying)
  - [Working with Strongly-Typed Models](#working-with-strongly-typed-models)
  - [Dynamic Content Access](#dynamic-content-access)
  - [Working with Linked Items](#working-with-linked-items)
  - [Rich Text Resolution](#rich-text-resolution)
  - [Multi-Language Support](#multi-language-support)
  - [Caching](#caching)
  - [Preview API](#preview-api)
  - [Asset Renditions](#asset-renditions)
  - [Custom Asset Domain](#custom-asset-domain)
  - [Image Transformation](#image-transformation)
- [Configuration Options](#configuration-options)
- [Important Considerations](#important-considerations)
- [Advanced Documentation](#advanced-documentation)
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

## Basic Usage

### Setting Up the Delivery Client

The SDK is designed to work with .NET's dependency injection container. Register the `IDeliveryClient` in your `Program.cs` or `Startup.cs`:

#### Basic Registration

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
}));
```

#### Registration from Configuration

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

#### Registration from Other DI Services

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

#### API Mode Helpers

The members that set more than one property come as extension methods on `DeliveryOptions`, usable inside
`Configure` and on any instance:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi("your-preview-api-key");   // or UseProductionApi(), UseProductionApi(secureAccessApiKey), UseCustomEndpoint(url)
}));
```

#### Source-Generated Type Provider (Recommended)

When you use the `[ContentTypeCodename]` attribute on your model classes (see [Generate Models](#generate-models)), the SDK's source generator automatically creates a `GeneratedTypeProvider`. The SDK auto-discovers this provider at runtime - no manual registration needed.

A model is a class or a record class. The SDK hydrates elements on the instance it deserialized, so a struct would be copied and its values lost; the generator reports `KDSG003` for one, and the client throws `NotSupportedException` if a struct reaches it another way.

> [!NOTE]
> `Kontent.Ai.Delivery.SourceGeneration` emits `ContentTypeCodenameAttribute` and generates `GeneratedTypeProvider` during compilation.
> If your models are generated into a separate project, reference `Kontent.Ai.Delivery.SourceGeneration` in that models project.

```csharp
// Just register the delivery client - type provider is auto-discovered
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
}));
```

The auto-discovery searches the entry assembly and its references for the generated provider.

For predictable auto-discovery, keep your attributed models in a single models project that references `Kontent.Ai.Delivery.SourceGeneration`.
If your models are intentionally split across multiple projects/compilations, register an explicit `ITypeProvider` yourself (for example one produced by the Kontent.ai model generator tool).

#### Registering a Custom Type Provider

If you need to override the auto-discovered provider or use a custom implementation, register your type provider on the collection - before the client, or on the builder's `Services`:

```csharp
services.AddDeliveryClient(delivery =>
{
    delivery.Services.AddSingleton<ITypeProvider, MyCustomTypeProvider>();
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
});
```

The SDK registers its default type provider with `TryAddSingleton`, so your registration takes precedence whether it comes before the client or through the builder.

#### Without Dependency Injection

For console applications, scripts, or scenarios where DI is not available, build the client with
`DeliveryClient.Create`. It takes the same builder as `AddDeliveryClient` and runs the same registration
inside a private container the client owns:

```csharp
// Simple usage with Production API
await using var client = DeliveryClient.Create(delivery => delivery.Options.Configure(o => o.EnvironmentId = "your-environment-id"));

// With Preview API (preview mode bypasses local cache reads/writes)
await using var previewClient = DeliveryClient.Create(delivery => delivery.Options.Configure(o =>
{
    o.EnvironmentId = "your-environment-id";
    o.UsePreviewApi("your-preview-api-key");
}));

// With Production API and in-memory caching (requires Kontent.Ai.Delivery.Caching package)
await using var cachedClient = DeliveryClient.Create(delivery =>
{
    delivery.Options.Configure(o => o.EnvironmentId = "your-environment-id");
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromMinutes(30));
});

// Or explicitly provide a type provider if needed
await using var typedClient = DeliveryClient.Create(delivery =>
{
    delivery.Services.AddSingleton<ITypeProvider>(new GeneratedTypeProvider());
    delivery.Options.Configure(o => o.EnvironmentId = "your-environment-id");
});

// From a pre-built options instance
await using var fromOptions = DeliveryClient.Create(new DeliveryOptions { EnvironmentId = "your-environment-id" });
```

The builder is one type in both hosting modes:
- `.Options` - the client's `OptionsBuilder<DeliveryOptions>` (`Configure`, `Bind`, `BindConfiguration`, ...)
- `.HttpClient` - the named `IHttpClientBuilder` the transport is built on (`ConfigurePrimaryHttpMessageHandler`, `AddHttpMessageHandler`, ...)
- `.ConfigureResilience(...)` - replaces the default resilience pipeline
- `.Services` and `.Name` - what anything else attaches to: a custom `ITypeProvider`, an `ILoggerFactory`, the caching package's `UseMemoryCache` / `UseHybridCache` / `UseCacheManager`

`Create` returns the concrete `DeliveryClient`, which owns the container it was built from - disposing
it tears that down, which is why the examples use `await using`. Keep the result as `var` (or
`DeliveryClient`); widening it to `IDeliveryClient` drops the disposal, because the interface
deliberately does not carry it. A client resolved from a container is owned by the container, so there
is nothing for you to dispose there. `IDeliveryClientFactory` is the other half of that split: it resolves
named clients from a container you own, while `Create` builds a standalone one over a container it owns.
Invalid options surface as `OptionsValidationException` from `Create`.

`UseCustomEndpoint(...)` applies the same endpoint to both Production and Preview URLs. In most real deployments these endpoints differ, so if you need both modes with custom domains, register separate clients (for example named clients) and configure each with its corresponding endpoint.

### Retrieving Content

#### Get a Single Item

```csharp
// By codename
var result = await client.GetItem("coffee_beverages_explained")
    .ExecuteAsync();

if (result.IsSuccess)
{
    var article = result.Value;
    Console.WriteLine($"Title: {article.System.Name}");
}
```

#### Get Multiple Items

```csharp
var result = await client.GetItems()
    .Limit(10)
    .ExecuteAsync();

if (result.IsSuccess)
{
    foreach (var item in result.Value.Items)
    {
        Console.WriteLine($"- {item.System.Name}");
    }
}
```

#### Get Items with Pagination

For large datasets, use the items feed for paginated enumeration with continuation tokens (e.g. for search index building, data synchronization, or bulk exports):

```csharp
// Option 1: Enumerate all items one-by-one using IAsyncEnumerable
await foreach (var item in client.GetItemsFeed().EnumerateAsync())
{
    Console.WriteLine($"Item: {item.System.Name}");
}

// Option 2: Manual page-by-page control using FetchNextPageAsync
var firstPage = await client.GetItemsFeed().ExecuteAsync();
if (firstPage.IsSuccess)
{
    foreach (var item in firstPage.Value.Items)
    {
        Console.WriteLine($"Item: {item.System.Name}");
    }

    // Fetch next page if available
    while (firstPage.Value.HasNextPage)
    {
        var nextPage = await firstPage.Value.FetchNextPageAsync();
        if (nextPage?.IsSuccess == true)
        {
            foreach (var item in nextPage.Value.Items)
            {
                Console.WriteLine($"Item: {item.System.Name}");
            }
            firstPage = nextPage;
        }
        else break;
    }
}

// Option 3: Page enumeration, with the continuation token for checkpointing
await foreach (var page in client.GetItemsFeed().EnumerateAsync().AsPages())
{
    foreach (var item in page.Items)
    {
        Console.WriteLine($"Item: {item.System.Name}");
    }

    Save(page.ContinuationToken);   // null on the last page — that means finished, not "start over"
}
```

`EnumerateAsync()` is a walk, not a request: a failed page throws `DeliveryRequestException` rather than ending the
sequence, so a partial result can never be mistaken for a complete one. Both views &mdash; items and `AsPages()` &mdash;
behave the same way. Where you want a failure as a value instead, use `ExecuteAsync()`, which returns an
`IDeliveryResult` like every other single request:

**one request returns a result; a walk returns an enumerable that throws.**

```csharp
// Resume a walk from a persisted token
var result = await client.GetItemsFeed<Article>().ExecuteAsync(savedToken);
if (result.IsSuccess)
{
    Process(result.Value.Items);
    Save(result.Value.ContinuationToken);   // null once the walk is finished
}
```

For standard skip/limit pagination with `GetItems()`, use `FetchNextPageAsync()` to iterate through pages:

```csharp
var firstPage = await client.GetItems<Article>()
    .Limit(10)
    .WithTotalCount()
    .ExecuteAsync();

if (firstPage.IsSuccess)
{
    // Process first page
    foreach (var item in firstPage.Value.Items)
    {
        Console.WriteLine($"Item: {item.System.Name}");
    }

    // Fetch next page if available
    if (firstPage.Value.HasNextPage)
    {
        var nextPage = await firstPage.Value.FetchNextPageAsync();
        // Continue processing...
    }
}
```

### Content Types and Elements

Content types define the structure of your content. The SDK provides methods to retrieve content type definitions and their elements.

#### Get a Single Content Type

```csharp
var result = await client.GetType("article").ExecuteAsync();

if (result.IsSuccess)
{
    var contentType = result.Value;
    Console.WriteLine($"Type: {contentType.System.Name}");
    Console.WriteLine($"Codename: {contentType.System.Codename}");

    // Access element definitions
    foreach (var (codename, element) in contentType.Elements)
    {
        Console.WriteLine($"  - {element.Name} ({element.Type})");
    }
}
```

#### Get Multiple Content Types

```csharp
var result = await client.GetTypes()
    .Limit(10)
    .ExecuteAsync();

if (result.IsSuccess)
{
    foreach (var contentType in result.Value.Types)
    {
        Console.WriteLine($"{contentType.System.Name}: {contentType.Elements.Count} elements");
    }

    // Pagination support
    if (result.Value.HasNextPage)
    {
        var nextPage = await result.Value.FetchNextPageAsync();
    }
}
```

#### Get a Specific Content Element

Retrieve a single element definition from a content type:

```csharp
var result = await client.GetContentElement("article", "body_copy").ExecuteAsync();

if (result.IsSuccess)
{
    var element = result.Value;
    Console.WriteLine($"Element: {element.Name}");
    Console.WriteLine($"Type: {element.Type}");
}
```

### Taxonomies

Taxonomies provide hierarchical classification for your content.

#### Get a Single Taxonomy Group

```csharp
var result = await client.GetTaxonomy("product_categories").ExecuteAsync();

if (result.IsSuccess)
{
    var taxonomy = result.Value;
    Console.WriteLine($"Taxonomy: {taxonomy.System.Name}");

    // Access hierarchical terms
    foreach (var term in taxonomy.Terms)
    {
        PrintTerm(term, 0);
    }
}

void PrintTerm(ITaxonomyTermDetails term, int indent)
{
    var prefix = new string(' ', indent * 2);
    Console.WriteLine($"{prefix}- {term.System.Name} ({term.System.Codename})");

    // Recursively print child terms
    foreach (var childTerm in term.Terms)
    {
        PrintTerm(childTerm, indent + 1);
    }
}
```

#### Get Multiple Taxonomy Groups

```csharp
var result = await client.GetTaxonomies()
    .Limit(10)
    .ExecuteAsync();

if (result.IsSuccess)
{
    foreach (var taxonomy in result.Value.Taxonomies)
    {
        Console.WriteLine($"{taxonomy.System.Name}: {taxonomy.Terms.Count} top-level terms");
    }
}
```

### Reference Lookups (Used In)

Find which content items reference a specific item or asset. This is useful for impact analysis before making changes.

`EnumerateAsync()` follows continuation tokens automatically. It is a walk, so a failed page throws
`DeliveryRequestException` — a truncated result can never pass for a complete one.

```csharp
await foreach (var usage in client.GetItemUsedIn("john_doe").EnumerateAsync())
{
    Console.WriteLine($"Referenced by: {usage.System.Name}");
}
```

Use `AsPages()` when you want the continuation token, and `ExecuteAsync()` when you want a failure as a value rather
than an exception:

```csharp
var result = await client.GetItemUsedIn("john_doe").ExecuteAsync();
if (!result.IsSuccess)
{
    Console.WriteLine($"Used-in lookup failed with {(int)result.StatusCode}: {result.Error?.Message}");
    return;
}

foreach (var usage in result.Value.Items)
{
    Console.WriteLine($"Referenced by: {usage.System.Name}");
}

// result.Value.ContinuationToken feeds the next call, or is null when finished.
```

#### Find Items Using a Content Item

```csharp
// Find all items that reference the "john_doe" author
await foreach (var usage in client.GetItemUsedIn("john_doe").EnumerateAsync())
{
    Console.WriteLine($"Referenced by: {usage.System.Name} ({usage.System.Type})");
}
```

#### Find Items Using an Asset

```csharp
// Find all items that use a specific asset
var assetCodename = "hero_image";
var usages = new List<IUsedInItem>();
await foreach (var usage in client.GetAssetUsedIn(assetCodename).EnumerateAsync())
{
    usages.Add(usage);
    Console.WriteLine($"Asset used in: {usage.System.Name}");
}

if (usages.Count == 0)
{
    Console.WriteLine("Asset is not used anywhere - safe to delete");
}
```

### Filtering and Querying

The SDK provides a type-safe filtering API with support for various operators:

#### Basic Filtering

```csharp
var result = await client.GetItems()
    .Where(f => f
        .System("type").IsEqualTo("article")
        // [contains] is for arrays (taxonomy/linked items/multiple choice), not strings.
        // See Delivery API docs: https://kontent.ai/learn/docs/apis/delivery-api/filtering-parameters?sl=1
        .Element("category").Contains("coffee"))
    .Limit(20)
    .ExecuteAsync();
```

> [!TIP]
> When using strongly-typed queries with source generation (e.g., `GetItems<Article>()`), the `system.type` filter is added automatically based on the `[ContentTypeCodename]` attribute. You only need manual type filtering for dynamic queries.

#### Incremental query composition (deferred execution)

The query is not sent until you call `ExecuteAsync()`, so you can build it up conditionally:

```csharp
var query = client.GetItems()
    .Limit(20);

if (onlyArticles)
{
    query = query.Where(f => f.System("type").IsEqualTo("article"));
}

if (!includeArchived)
{
    query = query.Where(f => f.System("collection").IsNotEqualTo("archived"));
}

if (onlyCoffee)
{
    query = query.Where(f => f.Element("category").Contains("coffee"));
}

var result = await query.ExecuteAsync();
```

#### Common Filter Operators

```csharp
var query = client.GetItems()
    .Where(f => f
        // Equality
        .System("type").IsEqualTo("product")
        .System("collection").IsNotEqualTo("archived")
        // Comparison (numbers, dates, strings)
        .Element("price").IsGreaterThan(100.0)
        .Element("rating").IsLessThanOrEqualTo(4.5)
        // Range (inclusive)
        .Element("price").IsWithinRange(50.0, 500.0)
        // Array membership
        .System("type").IsIn("article", "blog_post")
        // Multi-value element matching
        .Element("tags").ContainsAny("featured", "trending")
        .Element("categories").ContainsAll("tech", "news")
        // Null/empty checks
        .Element("description").IsNotEmpty());
```

#### Ordering and Pagination

```csharp
var result = await client.GetItems()
    .OrderBySystem("last_modified", OrderingMode.Descending)
    .Skip(0)
    .Limit(10)
    .ExecuteAsync();

// Order by an element; the generated codename constants work here too
var articles = await client.GetItems<Article>()
    .OrderByElement(Article.PublishDateCodename, OrderingMode.Descending)
    .ExecuteAsync();
```

`OrderByElement` and `OrderBySystem` add the `elements.` / `system.` prefix for you, the same way `Element()` and `System()` do in `Where`. `OrderBy("elements.publish_date")` still accepts a full path.

#### Getting Total Count

```csharp
var result = await client.GetItems()
    .WithTotalCount()
    .Limit(10)
    .ExecuteAsync();

if (result.IsSuccess)
{
    // Total count is returned in response pagination metadata
    Console.WriteLine($"Total items: {result.Value.Pagination.TotalCount}");
    Console.WriteLine($"Returned: {result.Value.Items.Count}");
}
```

#### Element Projection

Reduce response size and improve performance by selecting only the elements you need:

```csharp
// Include only specific elements
var result = await client.GetItems<Article>()
    .WithElements("title", "summary", "url_slug")
    .Limit(20)
    .ExecuteAsync();

// Exclude specific elements (get all except these)
var result = await client.GetItems<Article>()
    .WithoutElements("body_copy", "metadata")
    .Limit(20)
    .ExecuteAsync();
```

> [!TIP]
> For listing pages that only show titles and summaries, use `.WithElements()` to reduce payload size by 50-80%.

For more advanced filtering scenarios, see the [Advanced Filtering Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/advanced-filtering.md).

### Working with Strongly-Typed Models

The SDK supports strongly-typed models for compile-time safety and IntelliSense support. Using the SDK with strongly typed models is recommended.

#### Generate Models

Use the [Kontent.ai Model Generator](https://github.com/kontent-ai/dotnet/tree/main/src/model-generator) to generate C# classes from your content types:

```bash
dotnet tool install -g Kontent.Ai.ModelGenerator
KontentModelGenerator --environmentid <your-environment-id> --outputdir Models
```

#### Source Generation for Type Resolution

The [Kontent.ai Model Generator](https://github.com/kontent-ai/dotnet/tree/main/src/model-generator) automatically includes the `[ContentTypeCodename]` attribute on generated model classes. When combined with the source generation package, this provides:

- **Compile-time validation** - Duplicate codenames and invalid configurations are caught during build
- **Auto-discovered type provider** - No manual DI registration needed
- **Automatic type filtering** - Generic queries like `GetItems<Article>()` automatically add `system.type=article` filter
- **Build-time generation** - The source generator emits `ContentTypeCodenameAttribute` and generates `GeneratedTypeProvider` during build

Add the source generation package to enable these features:

```xml
<PackageReference Include="Kontent.Ai.Delivery.SourceGeneration" Version="<latest>" />
```

> [!IMPORTANT]
> Add `Kontent.Ai.Delivery.SourceGeneration` to the project that compiles your generated model classes.
> Keep the package version aligned with your `Kontent.Ai.Delivery` package version.

Generated models include the attribute automatically:

```csharp
// Generated by Kontent.ai Model Generator
using Kontent.Ai.Delivery.Attributes;

[ContentTypeCodename("article")]
public record Article
{
    public string Title { get; init; }
    public string Summary { get; init; }
    public RichTextContent BodyCopy { get; init; }
}

[ContentTypeCodename("product")]
public record Product
{
    public string Name { get; init; }
    public decimal Price { get; init; }
}
```

The source generator emits `ContentTypeCodenameAttribute` and produces a `GeneratedTypeProvider` at compile time with bi-directional lookup (codename ↔ Type). The SDK auto-discovers this provider at runtime.

> [!NOTE]
> Source generation runs per project/compilation. For the default auto-discovery path, use a single models project containing your attributed models.
> If you split models across multiple projects, prefer explicit `ITypeProvider` registration.

**Compile-time diagnostics:**
- `KDSG001`: Duplicate codename (error)
- `KDSG002`: Invalid codename - null, empty, or whitespace (error)
- `KDSG003`: Unsupported target type - interfaces, abstract classes and structs (error)

#### Use Strongly-Typed Models

```csharp
public record Article
{
    public string Title { get; set; }
    public string Summary { get; set; }
    public RichTextContent BodyCopy { get; set; }
    public DateTime PublishDate { get; set; }
    public IEnumerable<IEmbeddedContent> RelatedArticles { get; set; }
}

// Query with strong typing
var result = await client.GetItems<Article>()
    .WithLanguage("en-US")
    .ExecuteAsync();

if (result.IsSuccess)
{
    foreach (var article in result.Value.Items)
    {
        Console.WriteLine($"{article.Elements.Title} - {article.Elements.PublishDate}");
    }
}
```

> [!NOTE]
> When using source generation with `[ContentTypeCodename("article")]`, the SDK automatically adds `system.type=article` filter to generic queries like `GetItems<Article>()`. You don't need to manually filter by type.

### Dynamic Content Access

When you don't have strongly-typed models or need to access content dynamically, use the typeless query methods (`GetItem()`, `GetItems()`, `GetItemsFeed()`). You may also use them for runtime type resolution, if your project uses generated models.

> [!NOTE]
> Dynamic item/list queries (`GetItem()` and `GetItems()`) are intentionally non-cacheable because their final result type is resolved at runtime. Even with SDK caching configured, these queries always fetch from the API and return `IsCacheHit == false`.

#### Retrieve Content Without Type Parameters

```csharp
// Get a single item dynamically
var result = await client.GetItem("homepage").ExecuteAsync();

if (result.IsSuccess)
{
    var item = result.Value;
    Console.WriteLine($"Name: {item.System.Name}");
    Console.WriteLine($"Type: {item.System.Type}");

    // Access elements via pattern matching to IDynamicElements
    if (item is IContentItem<IDynamicElements> dynamicItem)
    {
        if (dynamicItem.Elements.TryGetValue("title", out var titleElement))
        {
            Console.WriteLine($"Title: {titleElement}");
        }
    }
}

// Get multiple items dynamically
var itemsResult = await client.GetItems()
    .Where(f => f.System("type").IsEqualTo("article"))
    .Limit(10)
    .ExecuteAsync();

if (itemsResult.IsSuccess)
{
    foreach (var item in itemsResult.Value.Items)
    {
        Console.WriteLine($"- {item.System.Name}");
    }
}
```

#### Runtime Type Resolution with Type Provider

When using source generation with `[ContentTypeCodename]` attributes, the SDK auto-discovers the generated `ITypeProvider`. Typeless queries automatically resolve items to their strongly-typed models at runtime:

```csharp
// Type provider is auto-discovered from source generation - no manual registration needed
services.AddDeliveryClient(delivery => delivery.Options.Configure(options => { ... }));

// Typeless queries return runtime-typed results
var result = await client.GetItem("on_roasts").ExecuteAsync();

if (result.IsSuccess)
{
    // Pattern match to access strongly-typed content
    switch (result.Value)
    {
        case IContentItem<Article> article:
            Console.WriteLine($"Article: {article.Elements.Title}");
            Console.WriteLine($"Summary: {article.Elements.Summary}");
            break;
        case IContentItem<Product> product:
            Console.WriteLine($"Product: {product.Elements.Name}");
            Console.WriteLine($"Price: ${product.Elements.Price}");
            break;
        default:
            // Fallback to dynamic access
            Console.WriteLine($"Unknown type: {result.Value.System.Type}");
            break;
    }
}
```

This is particularly useful for:
- **Mixed content listings**: Displaying articles, products, and other types together
- **Search results**: Content types vary based on search query
- **Webhook handlers**: Processing content where type isn't known until runtime

Linked items and rich text embedded content within runtime-typed items are also automatically resolved to their strongly-typed models.

#### When to Use Dynamic Access

Dynamic access is intended for edge cases where strongly-typed models are impractical:

- **Prototyping**: Quick exploration before generating models
- **Migration/sync tools**: Bulk processing across all content types
- **Admin utilities**: Generic content inspection tools

> [!TIP]
> For production applications, always use [strongly-typed models](#working-with-strongly-typed-models). They provide compile-time safety, IntelliSense support, and better maintainability.

### Working with Linked Items

Linked items (modular content) hydrate into strongly-typed embedded content. Because one element can
hold several content types, the property type is `IEnumerable<IEmbeddedContent>` and the concrete type
is recovered by pattern matching:

```csharp
public record Article
{
    [JsonPropertyName("title")]
    public string Title { get; init; }

    [JsonPropertyName("related_articles")]
    public IEnumerable<IEmbeddedContent>? RelatedArticles { get; init; }
}
```

#### Accessing Linked Items with Type Safety

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();
var article = result.Value.Elements;

foreach (var linkedItem in article.RelatedArticles!)
{
    // System metadata is on every item, whatever its type.
    Console.WriteLine($"{linkedItem.System.Codename} ({linkedItem.System.Type})");

    switch (linkedItem)
    {
        case IEmbeddedContent<Article> related:
            Console.WriteLine($"  Related article: {related.Elements.Title}");
            break;
        case IEmbeddedContent<Product> product:
            Console.WriteLine($"  Product: {product.Elements.Name} — ${product.Elements.Price}");
            break;
        default:
            // A type your models do not cover still arrives, with its System metadata.
            break;
    }
}
```

#### Filtering Linked Items by Type

Where you want one type rather than a branch per type, LINQ does it — `OfType<T>` for the wrapper,
plus a `Select` for the element model alone:

```csharp
var articles = article.RelatedArticles!.OfType<IEmbeddedContent<Article>>().ToList();

var articleElements = article.RelatedArticles!
    .OfType<IEmbeddedContent<Article>>()
    .Select(a => a.Elements)
    .ToList();
```

### Rich Text Resolution

Rich text elements may contain structured content that needs to be resolved prior to being rendered.

#### Basic HTML Rendering

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess)
{
    var article = result.Value.Elements;

    // Use default resolver
    var html = await article.BodyCopy.ToHtmlAsync();
}
```

#### Custom Link Resolution

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver("article", async (link, resolveChildren) =>
    {
        var url = $"/articles/{link.Metadata?.UrlSlug}";
        var innerHtml = await resolveChildren(link.Children);
        return $"<a href=\"{url}\">{innerHtml}</a>";
    })
    .WithContentItemLinkResolver("product", async (link, resolveChildren) =>
    {
        var url = $"/shop/{link.Metadata?.UrlSlug}";
        var innerHtml = await resolveChildren(link.Children);
        return $"<a href=\"{url}\">{innerHtml}</a>";
    })
    .Build();

var html = await article.BodyCopy.ToHtmlAsync(resolver);
```

#### Embedded Content Resolution

**Type-Safe Resolvers with Strongly-Typed Models:**

```csharp
var resolver = new HtmlResolverBuilder()
    // Type-safe resolver with compile-time checking
    .WithContentResolver<Tweet>(tweet =>
        $"<blockquote class=\"twitter-tweet\">{tweet.Elements.TweetText}<cite>@{tweet.Elements.AuthorHandle}</cite></blockquote>")
    // Async type-safe resolver
    .WithContentResolver<Video>(async video =>
    {
        var metadata = await _videoService.GetMetadataAsync(video.Elements.VideoId);
        return $"<div class=\"video-wrapper\"><iframe src=\"https://youtube.com/embed/{video.Elements.VideoId}\" title=\"{metadata.Title}\"></iframe></div>";
    })
    .Build();

var html = await article.BodyCopy.ToHtmlAsync(resolver);
```

**Codename-Based Resolvers:**

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver("tweet", content =>
    {
        // Requires manual casting
        if (content is IEmbeddedContent<Tweet> tweet)
        {
            return $"<blockquote>{tweet.Elements.TweetText}</blockquote>";
        }
        return string.Empty;
    })
    .Build();
```

Enable strict behavior when missing resolvers should fail fast:

```csharp
var resolver = new HtmlResolverBuilder()
    .ThrowOnMissingResolver()
    .WithContentResolver<Tweet>(tweet =>
        $"<blockquote>{tweet.Elements.TweetText}</blockquote>")
    .Build();
```

**Registering several resolvers:**

Chain one `WithContentResolver<T>` per model type. Each names its type once, and the resolver receives
`IEmbeddedContent<T>`, so there is nothing to cast.

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver<Tweet>(t => $"<blockquote>{t.Elements.TweetText}</blockquote>")
    .WithContentResolver<Video>(v => $"<iframe src=\"https://youtube.com/embed/{v.Elements.VideoId}\"></iframe>")
    .WithContentResolver<Quote>(q => $"<blockquote><p>{q.Elements.Text}</p><cite>{q.Elements.Author}</cite></blockquote>")
    .Build();
```

Codenames work the same way, and `WithContentResolvers` takes a dictionary or tuples when you already
have them in a collection:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolvers(
        ("tweet", content => $"<blockquote>{content.System.Name}</blockquote>"),
        ("hosted_video", content => $"<video data-item=\"{content.System.Id}\"></video>"))
    .Build();
```

> There are also `WithContentResolvers` overloads keyed by `Type`. They exist for model types you only
> have at runtime — found by scanning an assembly, say — and hand the resolver the non-generic
> `IEmbeddedContent`, because there is no type argument to give it. If you can name the type in source,
> use `WithContentResolver<T>` above.

#### Registering Resolver with Dependency Injection

To avoid creating the resolver at every call site, register `IHtmlResolver` in your DI container:

```csharp
// Program.cs - Register the resolver once
services.AddSingleton<IHtmlResolver>(sp => new HtmlResolverBuilder()
    .WithContentItemLinkResolver("article", async (link, resolveChildren) =>
    {
        var innerHtml = await resolveChildren(link.Children);
        return $"<a href=\"/articles/{link.Metadata?.UrlSlug}\">{innerHtml}</a>";
    })
    .WithContentResolver<Tweet>(tweet =>
        $"<blockquote>{tweet.Elements.TweetText}</blockquote>")
    .WithContentResolver<Video>(video =>
        $"<iframe src=\"https://youtube.com/embed/{video.Elements.VideoId}\"></iframe>")
    .Build());
```

Then inject and use it in your services:

```csharp
public class ArticleService
{
    private readonly IDeliveryClient _client;
    private readonly IHtmlResolver _resolver;

    public ArticleService(IDeliveryClient client, IHtmlResolver resolver)
    {
        _client = client;
        _resolver = resolver;
    }

    public async Task<string?> GetArticleHtmlAsync(string codename)
    {
        var result = await _client.GetItem<Article>(codename).ExecuteAsync();

        if (!result.IsSuccess)
            return null;

        return await result.Value.Elements.BodyCopy.ToHtmlAsync(_resolver);
    }
}
```

**Pattern Matching for Multiple Types:**

```csharp
// Access strongly-typed embedded content via pattern matching
foreach (var block in article.BodyCopy)
{
    switch (block)
    {
        case IEmbeddedContent<Tweet> tweet:
            Console.WriteLine($"Tweet: {tweet.Elements.TweetText}");
            break;
        case IEmbeddedContent<Video> video:
            Console.WriteLine($"Video: {video.Elements.Title}");
            break;
        case IEmbeddedContent<Quote> quote:
            Console.WriteLine($"Quote: {quote.Elements.Text}");
            break;
    }
}

// Or use extension methods for filtering
var tweets = article.BodyCopy.GetEmbeddedContent<Tweet>();
var tweetElements = article.BodyCopy.GetEmbeddedElements<Tweet>();
```

For advanced rich text scenarios including custom HTML nodes and complex resolution strategies, see the [Rich Text Customization Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/rich-text-customization.md).

### Multi-Language Support

Retrieve content in specific language variants:

#### Basic Language Variant Retrieval

```csharp
// Get Spanish version
var result = await client.GetItem("homepage")
    .WithLanguage("es-ES")
    .ExecuteAsync();

// Get all articles in German (strongly typed)
var articlesResult = await client.GetItems<Article>()
    .Where(f => f.System("type").IsEqualTo("article"))
    .WithLanguage("de-DE")
    .ExecuteAsync();
```

#### Language Fallbacks

Language fallbacks are configured in your Kontent.ai project. The SDK respects these settings automatically. If content is not available in the requested language, the SDK returns content according to your fallback configuration.

By default, `.WithLanguage("<lang>")` requests a language variant while still allowing fallbacks configured in Kontent.ai (this is equivalent to using the Delivery API `language=<lang>` parameter without also filtering by `system.language`).

To **disable language fallbacks** for list/feed queries (return only items that are actually translated into the requested language), use:

```csharp
var result = await client.GetItems<Article>()
    .WithLanguage("es-ES", LanguageFallbackMode.Disabled)
    .ExecuteAsync();
```

When `LanguageFallbackMode.Disabled` is used, the SDK automatically adds the equivalent of `system.language[eq]=<lang>` to the request (so the query uses both `language=<lang>` and `system.language=<lang>` as described in Kontent.ai docs).

> `GetItem(...)` single-item queries (typed and dynamic) do not support disabling language fallbacks. They only use `language=<lang>` and follow fallback behavior configured in Kontent.ai.

For list/feed queries, you can still achieve the same behavior manually by combining `.WithLanguage` and filtering on `system.language`, setting both to the desired language codename. See [Ignoring language fallbacks](https://kontent.ai/learn/develop/hello-world/get-localized-content/typescript#a-ignoring-language-fallbacks) in Kontent.ai documentation for more details.

#### Get Available Languages

```csharp
var result = await client.GetLanguages().ExecuteAsync();

if (result.IsSuccess)
{
    foreach (var language in result.Value.Languages)
    {
        Console.WriteLine($"{language.System.Name} ({language.System.Codename})");
    }
}
```

### Caching

The SDK supports both in-memory and hybrid (L1+L2) caching for improved performance. Caching is provided by the standalone `Kontent.Ai.Delivery.Caching` package:

```bash
dotnet add package Kontent.Ai.Delivery.Caching
```

#### Memory Cache

```csharp
// Single client scenario
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});

// Multi-client scenario - each named client attaches its own cache
services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure(options => { ... });
    delivery.UseMemoryCache(o => o.DefaultExpiration = TimeSpan.FromHours(1));
});
```

#### Hybrid Cache (Redis, SQL Server, etc.)

```csharp
// First, register your distributed cache implementation
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Single client scenario
services.AddDeliveryClient(delivery =>
{
    delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
    delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(2));
});
```

> [!IMPORTANT]
> **Running more than one instance? Register a backplane.** Part of the invalidation state lives in each
> instance rather than in the shared cache, so without a backplane whether one instance observes another's
> `InvalidateAsync` depends on the order they happened to read and invalidate in — an instance can go on
> serving content a webhook already evicted, until the entry expires by itself. Registering an
> `IFusionCacheBackplane` makes propagation reliable; the SDK picks it up from the container automatically.
>
> ```csharp
> services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
> services.AddFusionCacheStackExchangeRedisBackplane(o => o.Configuration = "localhost:6379");
> services.AddDeliveryClient(delivery =>
> {
>     delivery.Options.Configure(options => options.EnvironmentId = "your-environment-id");
>     delivery.UseHybridCache(o => o.DefaultExpiration = TimeSpan.FromHours(2));
> });
> ```
>
> A single-instance application needs no backplane.

#### Cache Options from Other DI Services

Use the `IServiceProvider` cache overloads when cache settings need to come from other registered services:

```csharp
services.Configure<SiteOptions>(configuration.GetSection("Site"));

services.AddDeliveryClient("production", delivery =>
{
    delivery.Options.Configure<IServiceProvider>((options, sp) =>
    {
        var site = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
        options.EnvironmentId = site.EnvironmentId;
    });

    delivery.UseMemoryCache((sp, options) =>
    {
        var site = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
        options.DefaultExpiration = site.CacheExpiration;
        options.IsFailSafeEnabled = true;
    });
});
```

For callback timing and lifetime guidance, see [Configuring Cache Options from DI Services](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md#configuring-cache-options-from-di-services).

#### What Gets Cached

Caching is transparent: once a client has a cache attached, every cacheable query is cached, keyed by
its parameters. Three things are deliberately left out:

- **Dynamic queries.** `GetItem()` and `GetItems()` resolve their result type at runtime, so they are never cached and always report `IsCacheHit == false`.
- **Preview clients.** A client with `UsePreviewApi = true` bypasses cache reads and writes entirely, so preview stays fresh.
- **`WaitForLoadingNewContent(true)` queries.** That request path skips both the read and the write.

`SecureAccessApiKey` and `PreviewApiKey` are not part of cache key identity — secure access only gates
*which* published content you may read, and preview never reaches the cache at all.

Override the TTL for one query when it needs a different one:

```csharp
var result = await client.GetItem<Article>("my-article")
    .WithCacheExpiration(TimeSpan.FromMinutes(5))
    .ExecuteAsync();
```

`InvalidateAsync` returns `Task<bool>` — `false` means the invalidation did not take, which is worth
acting on in a webhook endpoint (see below).

The caching guide covers what this section leaves out: [tuning the underlying FusionCache
instance](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md#reaching-into-fusioncache),
[writing a custom cache manager](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md#custom-cache-manager)
and attaching it with `UseCacheManager`, [cache key
shape](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md#cache-keys),
and [expiration strategies](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md#expiration-strategies).

#### Detecting Cache Hits

Every result reports where it came from, on `ResponseSource`:

| `ResponseSource` | Meaning | `IsCacheHit` |
|---|---|---|
| `Origin` | The Delivery API answered | `false` |
| `Cdn` | The Delivery CDN answered from its own cache (Fastly `X-Cache: HIT`) | `false` |
| `Cache` | The SDK's local cache answered | `true` |
| `FailSafe` | The SDK served a stale entry because the origin was unreachable | `true` |

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess)
{
    switch (result.ResponseSource)
    {
        case ResponseSource.Cache:
        case ResponseSource.FailSafe:
            // Metadata describing a request is null here, because no request was made.
            Console.WriteLine($"Served from SDK cache (stale: {result.ResponseSource is ResponseSource.FailSafe})");
            break;
        default:
            Console.WriteLine($"{result.ResponseSource} answered {result.RequestUrl}");
            break;
    }
}
```

`IsCacheHit` is the coarse view of the same fact — `true` for `Cache` and `FailSafe`, and the property to
reach for when all you need is "did this cost a request". The SDK reads the CDN's `X-Cache` header for you,
so there is no need to inspect `ResponseHeaders` yourself to tell `Cdn` from `Origin`.

#### Dependency Keys for Output Caching

Every delivery result exposes `DependencyKeys` — the canonical dependency keys describing which content entities the response depends on. These keys enable downstream cache invalidation scenarios such as ASP.NET output-cache tagging:

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess && result.DependencyKeys is { } keys)
{
    // keys contains: item_my-article, item_linked-author, asset_xxx, taxonomy_yyy, etc.
    // Use them to tag your output cache, CDN surrogate keys, etc.
}
```

Dependency keys are collected regardless of whether SDK caching is configured. The key formats match the SDK's internal cache invalidation keys (see [Webhook Invalidation Pattern](#webhook-invalidation-pattern-for-lists)).

#### Webhook Invalidation Pattern for Lists

Typed listing queries carry a synthetic scope dependency alongside their entity keys:

- `GetItems<T>()` → `DeliveryCacheDependencies.ItemsListScope`
- `GetTypes()` → `DeliveryCacheDependencies.TypesListScope`
- `GetTaxonomies()` → `DeliveryCacheDependencies.TaxonomiesListScope`

A webhook invalidates both the entity key and the relevant scope. Type and taxonomy events also
invalidate the items-list scope, because a membership change can affect an empty or projected listing
that carries no matching detail key. `DeliveryCacheDependencies` composes the keys exactly as the SDK
tags them:

```csharp
using Kontent.Ai.Delivery.Abstractions;

var cacheManager = serviceProvider.GetRequiredService<IDeliveryCacheManager>();

await cacheManager.InvalidateAsync(
    [DeliveryCacheDependencies.ForItem(itemCodename), DeliveryCacheDependencies.ItemsListScope]);

await cacheManager.InvalidateAsync(
    [DeliveryCacheDependencies.ForType(typeCodename), DeliveryCacheDependencies.TypesListScope, DeliveryCacheDependencies.ItemsListScope]);

// For a term event the payload names the term; the key is the group's.
await cacheManager.InvalidateAsync(
    [DeliveryCacheDependencies.ForTaxonomy(taxonomyGroupCodename), DeliveryCacheDependencies.TaxonomiesListScope, DeliveryCacheDependencies.ItemsListScope]);

// ForAsset reaches rich-text usages. An asset held in an asset element carries no asset id, so those
// items are found through the used-in lookup - which is why this overload takes the client.
await cacheManager.InvalidateAssetAsync(client, assetCodename, assetId);
```

The manager resolves unkeyed for the default client, keyed by name for a named one, and as
`CacheManager` on a client built by `DeliveryClient.Create`.

You rarely need to compose keys by hand. [`Kontent.Ai.AspNetCore`](https://github.com/kontent-ai/dotnet/tree/main/src/aspnetcore)
maps the payload Kontent.ai actually sends, routes asset events through the used-in lookup in the same
call, and validates the request signature in front of it — its
[Cache invalidation](https://github.com/kontent-ai/dotnet/blob/main/src/aspnetcore/README.md#cache-invalidation)
section has the complete endpoint, and the [caching
guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md#webhook-based-invalidation)
covers what invalidation does not reach.

With fail-safe on, an invalidated entry may still be served stale while the origin is unreachable; an
answer from the origin — a `404` for an unpublished item, say — drops it.

#### Purging the SDK Cache

Built-in cache managers can invalidate **all** entries at once through the optional `IDeliveryCachePurger`
capability. Language webhook events need this, because a language change has no key of its own.

```csharp
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;

// A named client's manager is keyed by its name; the default client's resolves unkeyed as well.
var cacheManager = serviceProvider.GetRequiredKeyedService<IDeliveryCacheManager>("production");
if (cacheManager is IDeliveryCachePurger purger)
{
    await purger.PurgeAsync();                    // permanently removes all entries
    await purger.PurgeAsync(allowFailSafe: true); // expires them, keeping fail-safe fallback data
}
```

Both modes throw if a distributed clear-marker write or a configured backplane publication fails, or is
skipped by an open circuit breaker — local entries may already be gone by then. Normal completion is not
an acknowledgment from every other node; ordinary cache reads stay fail-open. A custom cache manager that
does not implement `IDeliveryCachePurger` needs provider-specific tooling or key-prefix rotation instead.

> [!IMPORTANT]
> Runtime option changes on an already-cached client do not invalidate existing entries. After changing `EnvironmentId` or `DefaultRenditionPreset`, purge the cache (or recreate the client) before relying on the new setting.

For invalidation strategy, multi-tenant scenarios and the full behaviour matrix, see the [Caching
Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md).

### Preview API

The Preview API allows you to retrieve unpublished content for preview purposes.

#### Enable Preview API

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi = true;
    options.PreviewApiKey = "your-preview-api-key";
}));
```

When `UsePreviewApi` is enabled, the SDK always bypasses local cache reads/writes for that client, even if a cache manager is registered. This keeps preview responses fresh by default.

#### Dynamic Switching (Production vs Preview)

You can configure named clients for different environments:

```csharp
services.AddDeliveryClient("production", delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi = false;
}));

services.AddDeliveryClient("preview", delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.UsePreviewApi = true;
    options.PreviewApiKey = "your-preview-api-key";
}));

// Inject factory and get appropriate client
var factory = serviceProvider.GetRequiredService<IDeliveryClientFactory>();
var client = isPreviewMode ? factory.Get("preview") : factory.Get("production");
```

For more on named clients and multi-environment scenarios, see the [Multi-Client Scenarios Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/multi-client-scenarios.md).

### Asset Renditions

Assets can have pre-configured renditions (image presets) defined in Kontent.ai. Access these directly without applying additional transformations.

#### Accessing Asset Renditions

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess)
{
    var article = result.Value.Elements;

    foreach (var asset in article.TeaserImage)
    {
        // Original asset URL
        Console.WriteLine($"Original: {asset.Url}");
        Console.WriteLine($"Size: {asset.Width}x{asset.Height}");

        // Access pre-configured renditions
        if (asset.Renditions.TryGetValue("thumbnail", out var thumbnail))
        {
            Console.WriteLine($"Thumbnail: {thumbnail.Url}");
            Console.WriteLine($"Thumbnail size: {thumbnail.Width}x{thumbnail.Height}");
        }

        if (asset.Renditions.TryGetValue("hero", out var hero))
        {
            Console.WriteLine($"Hero: {hero.Url}");
        }
    }
}
```

#### Default Rendition Preset

Configure a default rendition preset to use across all asset URLs:

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.DefaultRenditionPreset = "web";  // Apply "web" preset to all assets
}));
```

When set, all asset URLs returned by the SDK will automatically include the specified rendition preset's transformations.

For named clients, `DefaultRenditionPreset` is resolved per client configuration. This means production/preview (or any named clients) can use different default presets independently.

When query caching is enabled, changing `DefaultRenditionPreset` on an existing client does not invalidate already-cached entries. Purge cache (or recreate the client) if you need the new default rendition to apply immediately.

### Custom Asset Domain

If you serve assets through a custom CDN or domain (e.g. for branding, geo-routing, or security), the SDK can rewrite all asset URLs — including inline images in rich text — to use your domain while preserving the original path and query string.

#### Configuration

```csharp
services.AddDeliveryClient(delivery => delivery.Options.Configure(options =>
{
    options.EnvironmentId = "your-environment-id";
    options.CustomAssetDomain = "https://assets.example.com";
}));
```

When set, all asset URLs returned by the SDK are rewritten from the default Kontent.ai host (e.g. `https://assets-eu-01.kc-usercontent.com/...`) to your custom domain (e.g. `https://assets.example.com/...`). This applies to:

- Asset element URLs (`IAsset.Url`)
- Inline image URLs in rich text content (`IInlineImage.Url`)
- `<img>` tag `src` attributes in resolved rich text HTML

The domain must be a root URL without a path, query string, or fragment. The SDK validates this at configuration time and throws `ArgumentException` for invalid values.

> [!NOTE]
> `CustomAssetDomain` and `DefaultRenditionPreset` work together — rendition query strings are appended after the domain rewrite.

### Image Transformation

`ImageUrlBuilder` rewrites an image URL served from Kontent.ai so the CDN resizes, crops and re-encodes
on the fly — no second copy of the asset to store. It ships in `Kontent.Ai.Urls`, which
`Kontent.Ai.Delivery` already brings in.

```csharp
using Kontent.Ai.Urls.ImageTransformation;

var optimizedHero = new ImageUrlBuilder(article.HeroImage.Url)
    .WithWidth(1920)
    .WithHeight(1080)
    .WithFitMode(ImageFitMode.Crop)
    .WithFocalPointCrop(0.5, 0.4, 1.0)
    .WithFormat(ImageFormat.Webp)
    .WithQuality(80)
    .Url;
```

Every method returns the builder, so transformations chain in any order; `Url` renders the result.

#### Available Transformations

| Method | Effect |
|---|---|
| `WithWidth(w)` / `WithHeight(h)` | Target dimensions in pixels |
| `WithDpr(ratio)` | Device pixel ratio — `WithWidth(400).WithDpr(2.0)` serves an 800px image |
| `WithFitMode(mode)` | How the image meets those dimensions: `Clip` (fit inside, the default), `Scale` (stretch exactly, may distort), `Crop` (fill and trim the excess) |
| `WithRectangleCrop(x, y, w, h)` | Extract one region |
| `WithFocalPointCrop(x, y, zoom)` | Crop around a point, `x`/`y` normalized to `0`–`1` |
| `WithFormat(format)` | Re-encode — see the format table below |
| `WithAutomaticFormat(fallback)` | WebP where the browser advertises support, `fallback` elsewhere |
| `WithQuality(1-100)` | Compression quality for lossy formats |
| `WithCompression(mode)` | `ImageCompression.Lossless` / `.Lossy`, for WebP |

#### Available Formats

| Format | Enum Value | Description |
|--------|------------|-------------|
| GIF | `ImageFormat.Gif` | Animated image support |
| PNG | `ImageFormat.Png` | Lossless with transparency |
| PNG8 | `ImageFormat.Png8` | 8-bit palette PNG |
| JPEG | `ImageFormat.Jpg` | Lossy compression |
| Progressive JPEG | `ImageFormat.Pjpg` | JPEG with progressive loading |
| WebP | `ImageFormat.Webp` | Modern format, best compression |

> [!TIP]
> Rendering in Razor? [`Kontent.Ai.AspNetCore`](https://github.com/kontent-ai/dotnet/tree/main/src/aspnetcore)'s `<img-asset>` tag helper applies these transformations and builds a responsive `srcset` for you.

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

## Important Considerations

### API Rate Limits

Kontent.ai enforces rate limits on API requests. The SDK includes built-in retry logic to handle transient failures, but you should:

- **Implement caching** as your first line of defense against rate limits
- Monitor your API usage in the Kontent.ai dashboard
- Use the items feed (`GetItemsFeed()`) for efficient bulk operations

### Depth Parameter

The Delivery API has default depth limitations for linked content:

```csharp
// Control how many levels of linked items to retrieve
var result = await client.GetItem("article")
    .Depth(2)  // Default is typically 1
    .ExecuteAsync();
```

**Important**: Higher depth values increase response size and processing time. Only increase when necessary.

### Preview API Security

**Never expose Preview API keys in client-side code.** Preview API keys should only be used in server-side applications. For web applications, implement a server-side preview endpoint that uses the Preview API on behalf of authenticated users.

### Caching Considerations

- **Cache identity** should be isolated per client/environment via named clients and key prefixes. Within a cache namespace, keys are based on query shape (for example query params, language, filters)
- **Memory cache** can lead to memory pressure with large content - monitor your application's memory usage
- **Hybrid cache** is recommended for production scenarios with multiple application instances
- Always implement **cache invalidation** strategies, ideally using webhooks
- **Cache hit semantics**: When `IsCacheHit` is `true`, properties like `ResponseHeaders` and `RequestUrl` are not available (null). Use `IsCacheHit` to differentiate between API responses and cached results

### Strong Typing Synchronization

When using generated models:

- **Regenerate models** whenever content types change in Kontent.ai
- Handle optional properties with nullable types
- Consider versioning strategies if you have long-running deployments
- **Source generation benefits**: When using `[ContentTypeCodename]` attributes, the compiler catches duplicate codenames and invalid configurations at build time (diagnostics KDSG001-003)

### Error Handling

The SDK uses a result pattern instead of throwing exceptions for API errors. This makes error handling explicit and predictable.

#### Checking for Errors

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

#### IError Properties

| Property | Description |
|----------|-------------|
| `Message` | Human-readable error description |
| `RequestId` | Unique request ID for Kontent.ai support |
| `ErrorCode` | Kontent.ai-specific error code |
| `SpecificCode` | More specific error code |
| `Exception` | Underlying exception (for network errors, etc.) |

### Response Metadata

Every API response includes metadata for debugging, cache control, and monitoring.

#### Accessing Response Metadata

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess)
{
    // Request URL for debugging
    Console.WriteLine($"Request URL: {result.RequestUrl}");

    // HTTP status code
    Console.WriteLine($"Status: {result.StatusCode}");

    // Where the response came from - see Detecting Cache Hits
    Console.WriteLine($"Source: {result.ResponseSource}");

    // Check if newer content might be available
    if (result.HasStaleContent)
    {
        Console.WriteLine("Content may be stale - newer version exists");
    }
}
```

#### IDeliveryResult Properties

| Property | Description |
|----------|-------------|
| `IsSuccess` | Whether the request succeeded |
| `Value` | The response content (when successful) |
| `Error` | Error details (when failed) |
| `StatusCode` | HTTP status code |
| `RequestUrl` | Full request URL for debugging (null for cache hits) |
| `ResponseHeaders` | HTTP response headers (null for cache hits) |
| `ResponseSource` | Which tier answered: `Origin`, `Cdn`, `Cache` or `FailSafe` — see [Detecting Cache Hits](#detecting-cache-hits) |
| `IsCacheHit` | Whether response was served from SDK cache (`Cache` or `FailSafe`) |
| `HasStaleContent` | Whether newer content may be available |
| `DependencyKeys` | Canonical dependency keys for output-cache tagging (null when not collected) |

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

## Advanced Documentation

For more advanced scenarios and in-depth guides, explore the following documentation:

- **[Advanced Filtering](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/advanced-filtering.md)** - Complex queries, combining filters, performance optimization
- **[Rich Text Customization](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/rich-text-customization.md)** - Custom resolvers, URL patterns, async resolution
- **[Caching Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/caching-guide.md)** - Cache strategies, invalidation, webhook integration
- **[Multi-Client Scenarios](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/multi-client-scenarios.md)** - Named clients, multi-tenant architectures
- **[Performance Optimization](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/performance-optimization.md)** - Query optimization, monitoring, best practices
- **[Extensibility Guide](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/extensibility-guide.md)** - Custom type providers, property mappers, SDK extension points
- **[Upgrade Guides](https://github.com/kontent-ai/dotnet/tree/main/src/delivery/docs/upgrade)** - One per major: [18 → 19](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade/18-to-19.md), [19 → 20](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade/19-to-20.md)

## Contributing

Contributions are welcome. Use [GitHub Issues](https://github.com/kontent-ai/dotnet/issues) for bug reports and feature requests, and open pull requests in this repository for code contributions.

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md) file for details.

---

**Questions or feedback?** Visit our [GitHub Issues](https://github.com/kontent-ai/dotnet/issues) or check the [Kontent.ai Developer Hub](https://kontent.ai/learn/docs).
