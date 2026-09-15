# Querying the Delivery API

Everything you can ask the Delivery API for, and how to shape the request: items, types, taxonomies,
reference lookups, filtering, ordering, projection, languages, and what comes back on the result.

Registration and configuration live in the [README](../README.md); this guide assumes you have an
`IDeliveryClient`.

## Table of Contents

- [Retrieving Content](#retrieving-content)
- [Content Types and Elements](#content-types-and-elements)
- [Taxonomies](#taxonomies)
- [Reference Lookups (Used In)](#reference-lookups-used-in)
- [Filtering and Querying](#filtering-and-querying)
- [Multi-Language Support](#multi-language-support)
- [Response Metadata](#response-metadata)

## Retrieving Content

The examples below use the generic form — `GetItem<Article>(…)`, `GetItems<Article>()` — which is the
recommended one: it hydrates your [generated models](models.md), gives you compile-time names, and adds
the `system.type` filter for you.

Every one of them has a non-generic overload, and dropping the type argument does **not** mean giving up
typing. With generated models in the project, the type provider resolves each item to its model at
*runtime* instead, and you recover it by pattern matching — see
[Runtime Type Resolution](models.md#runtime-type-resolution-with-type-provider). Reach for the
non-generic form when:

- **the type genuinely varies** — a mixed listing, a search result, a webhook handler;
- **you do not need the model at all** — a script, a one-off probe, or anything that only reads `System` metadata.

> [!NOTE]
> The non-generic `GetItem()` and `GetItems()` are never cached, because their result type is resolved per response. They always reach the API and report `IsCacheHit == false`.

### Get a Single Item

```csharp
// By codename
var result = await client.GetItem<Article>("coffee_beverages_explained")
    .ExecuteAsync();

if (result.IsSuccess)
{
    Console.WriteLine($"{result.Value.System.Name}: {result.Value.Elements.Title}");
}
```

Without the type argument the same call returns an `IContentItem`, resolved to its model at runtime
where one exists:

```csharp
var result = await client.GetItem("coffee_beverages_explained").ExecuteAsync();
Console.WriteLine(result.Value?.System.Name);
```

### Get Multiple Items

```csharp
var result = await client.GetItems<Article>()
    .Limit(10)
    .ExecuteAsync();

if (result.IsSuccess)
{
    foreach (var item in result.Value.Items)
    {
        Console.WriteLine($"- {item.Elements.Title}");
    }
}
```

### Get Items with Pagination

Two paging models, for two different jobs.

**The items feed** walks the whole set with continuation tokens — search index building, data
synchronization, bulk export:

```csharp
// Every item, one by one.
await foreach (var item in client.GetItemsFeed<Article>().EnumerateAsync())
{
    Console.WriteLine($"Item: {item.Elements.Title}");
}

// Page by page, when you want the continuation token for checkpointing.
await foreach (var page in client.GetItemsFeed<Article>().EnumerateAsync().AsPages())
{
    foreach (var item in page.Items)
    {
        Console.WriteLine($"Item: {item.System.Name}");
    }

    Save(page.ContinuationToken);   // null on the last page — that means finished, not "start over"
}
```

`EnumerateAsync()` is a walk, not a request: a failed page throws `DeliveryRequestException` rather than
ending the sequence, so a partial result can never be mistaken for a complete one. Both views — items and
`AsPages()` — behave the same way. Where you want a failure as a value instead, `ExecuteAsync()` returns an
`IDeliveryResult` like every other single request, and takes a saved token to resume from:

**one request returns a result; a walk returns an enumerable that throws.**

```csharp
var result = await client.GetItemsFeed<Article>().ExecuteAsync(savedToken);
if (result.IsSuccess)
{
    Process(result.Value.Items);
    Save(result.Value.ContinuationToken);   // null once the walk is finished
}
```

**Skip/limit paging** suits a page of results you are about to render:

```csharp
var firstPage = await client.GetItems<Article>()
    .Limit(10)
    .WithTotalCount()
    .ExecuteAsync();

if (firstPage.IsSuccess && firstPage.Value.HasNextPage)
{
    var nextPage = await firstPage.Value.FetchNextPageAsync();
}
```

`FetchNextPageAsync()` is available on every listing response — items, types and taxonomies alike.

## Content Types and Elements

Content types define the structure of your content. The SDK provides methods to retrieve content type definitions and their elements.

### Get a Single Content Type

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

### Get Multiple Content Types

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

### Get a Specific Content Element

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

## Taxonomies

Taxonomies provide hierarchical classification for your content.

### Get a Single Taxonomy Group

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

### Get Multiple Taxonomy Groups

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

## Reference Lookups (Used In)

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

### Find Items Using a Content Item

```csharp
// Find all items that reference the "john_doe" author
await foreach (var usage in client.GetItemUsedIn("john_doe").EnumerateAsync())
{
    Console.WriteLine($"Referenced by: {usage.System.Name} ({usage.System.Type})");
}
```

### Find Items Using an Asset

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

## Filtering and Querying

The SDK provides a type-safe filtering API with support for various operators:

### Basic Filtering

```csharp
var result = await client.GetItems<Article>()
    .Where(f => f
        // [contains] is for arrays (taxonomy/linked items/multiple choice), not strings.
        // See Delivery API docs: https://kontent.ai/learn/docs/apis/delivery-api/filtering-parameters?sl=1
        .Element("category").Contains("coffee"))
    .Limit(20)
    .ExecuteAsync();
```

> [!TIP]
> No `system.type` filter above: `GetItems<Article>()` adds it from the `[ContentTypeCodename]` attribute. Filter on `system.type` yourself only in a non-generic query.

### Property paths

The DSL builds property paths for you:

- `System("<property>")` → `system.<property>`
  - Examples: `system.type`, `system.codename`, `system.language`, `system.last_modified`, `system.collection`, `system.workflow_step`
- `Element("<codename>")` → `elements.<codename>`
  - Examples: `elements.title`, `elements.price`, `elements.tags`, `elements.publish_date`

### Operator reference

#### Equality

- `IsEqualTo(...)` → `[eq]`
- `IsNotEqualTo(...)` → `[neq]`

```csharp
// system.type[eq]=article
.Where(f => f.System("type").IsEqualTo("article"))

// elements.status[neq]=draft
.Where(f => f.Element("status").IsNotEqualTo("draft"))
```

#### Comparison

- `IsLessThan(...)` → `[lt]`
- `IsLessThanOrEqualTo(...)` → `[lte]`
- `IsGreaterThan(...)` → `[gt]`
- `IsGreaterThanOrEqualTo(...)` → `[gte]`

```csharp
.Where(f => f.Element("price").IsGreaterThan(100.0))
.Where(f => f.System("last_modified").IsLessThanOrEqualTo(DateTime.UtcNow.AddDays(-7)))
```

#### Range (inclusive)

- `IsWithinRange(lower, upper)` → `[range]` with `lower,upper`

```csharp
.Where(f => f.Element("price").IsWithinRange(100.0, 500.0))
.Where(f => f.System("last_modified").IsWithinRange(DateTime.Parse("2024-01-01"), DateTime.Parse("2024-06-30")))
```

#### Collection membership

- `IsIn(...)` → `[in]`
- `IsNotIn(...)` → `[nin]`

```csharp
.Where(f => f.System("type").IsIn("article", "blog_post", "news"))
.Where(f => f.Element("rating").IsIn(4.0, 5.0))
```

#### Arrays

- `Contains("...")` → `[contains]` (array contains a value)
- `ContainsAny(...)` → `[any]` (array contains at least one)
- `ContainsAll(...)` → `[all]` (array contains all)

```csharp
.Where(f => f.Element("category").Contains("coffee"))
.Where(f => f.Element("tags").ContainsAny("featured", "trending"))
.Where(f => f.Element("required_features").ContainsAll("warranty", "manual"))
```

#### Empty checks

- `IsEmpty()` → `[empty]`
- `IsNotEmpty()` → `[nempty]`

```csharp
.Where(f => f.Element("seo_description").IsEmpty())
.Where(f => f.Element("summary").IsNotEmpty())
```

### Ordering and Pagination

```csharp
var result = await client.GetItems<Article>()
    .OrderBySystem("last_modified", OrderingMode.Descending)
    .Skip(0)
    .Limit(10)
    .ExecuteAsync();

// Order by an element; the generated codename constants avoid a magic string
var articles = await client.GetItems<Article>()
    .OrderByElement(Article.PostDateCodename, OrderingMode.Descending)
    .ExecuteAsync();
```

`OrderByElement` and `OrderBySystem` add the `elements.` / `system.` prefix for you, the same way `Element()` and `System()` do in `Where`. `OrderBy("elements.publish_date")` still accepts a full path.

### Getting Total Count

```csharp
var result = await client.GetItems<Article>()
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

### Element Projection

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

### Depth

The Delivery API resolves linked content to a limited depth - one level unless you ask for more:

```csharp
var result = await client.GetItem<Article>("article")
    .Depth(2)
    .ExecuteAsync();
```

Higher values increase response size and processing time, so raise it only where a view genuinely
needs the deeper level.

### Combining filters with other query parameters

```csharp
var result = await client.GetItems<Article>()
    .Where(f => f.Element("tags").ContainsAny("beginner", "intermediate"))
    .WithLanguage("en-US")
    .WithElements("title", "summary")
    .Depth(2)
    .OrderBySystem("last_modified", OrderingMode.Descending)
    .Limit(10)
    .ExecuteAsync();
```

### Incremental query composition (deferred execution)

The query is not sent until you call `ExecuteAsync()`, so you can build it up conditionally. This is a
case where the non-generic form earns its keep: the content type is itself one of the conditions, so
there is no type argument to give:

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

### Troubleshooting

- **Unexpected no-results**: remember filters are ANDed; temporarily comment out filters to isolate the restrictive one.
- **Special characters**: string values are URL-encoded automatically (e.g. `&` becomes `%26`).
- **Date filtering**: the SDK serializes all `DateTime` filter values to UTC (`Z`). `Local` values are converted to UTC; `Unspecified` values are treated as UTC (no offset conversion). Prefer `DateTime.UtcNow`/UTC values, or use `DateTimeOffset` and convert to UTC explicitly before filtering. The Delivery API compares date-time strings; be explicit about bounds (see Delivery docs: [Filtering parameters](https://kontent.ai/learn/docs/apis/delivery-api/filtering-parameters)).

## Multi-Language Support

Retrieve content in specific language variants:

### Basic Language Variant Retrieval

```csharp
// Get the Spanish variant
var result = await client.GetItem<Article>("coffee_beverages_explained")
    .WithLanguage("es-ES")
    .ExecuteAsync();

// Every article in German
var articlesResult = await client.GetItems<Article>()
    .WithLanguage("de-DE")
    .ExecuteAsync();
```

### Language Fallbacks

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

### Get Available Languages

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

## Response Metadata

Every API response includes metadata for debugging, cache control, and monitoring.

### Accessing Response Metadata

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

### IDeliveryResult Properties

| Property | Description |
|----------|-------------|
| `IsSuccess` | Whether the request succeeded |
| `Value` | The response content (when successful) |
| `Error` | Error details (when failed) |
| `StatusCode` | HTTP status code |
| `RequestUrl` | Full request URL for debugging (null for cache hits) |
| `ResponseHeaders` | HTTP response headers (null for cache hits) |
| `ResponseSource` | Which tier answered: `Origin`, `Cdn`, `Cache` or `FailSafe` — see [Detecting Cache Hits](caching-guide.md#detecting-cache-hits) |
| `IsCacheHit` | Whether response was served from SDK cache (`Cache` or `FailSafe`) |
| `HasStaleContent` | Whether newer content may be available |
| `DependencyKeys` | Canonical dependency keys for output-cache tagging (null when not collected) |
