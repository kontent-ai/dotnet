# Kontent.ai Management SDK for .NET

[![NuGet](https://img.shields.io/nuget/v/Kontent.Ai.Management?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Management)
[![Downloads](https://img.shields.io/nuget/dt/Kontent.Ai.Management?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Management)

> [!WARNING]
> **This is a beta release.** The SDK is undergoing a ground-up modernization, and this README documents the new, **modernized API** — result-based return types, materialized listings, `System.Text.Json` serialization, and strongly-typed content models. While in beta it is published as a **prerelease**, and **breaking changes may land between prereleases** until the first stable major version ships. Pin an exact version if you need stability during the beta.
>
> **For production, use the latest stable release** — the `8.x` line, which exposes the previous API and is installed without the `--prerelease` flag. See the [package on NuGet][nuget-url] for the current stable version.
>
> Migrating from `8.x`? Start with the [upgrade guide](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/upgrade-guide.md).

The official .NET SDK for the [Kontent.ai Management API](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/) — programmatic read/write access to your Kontent.ai projects and environments: content items, language variants, content models, assets, taxonomies, workflows, environments, and more.

## Table of Contents

- [Installation](#installation)
- [Upgrade Guide](#upgrade-guide)
- [Quick Start](#quick-start)
- [Creating the Client](#creating-the-client)
  - [With Dependency Injection](#with-dependency-injection)
  - [Standalone](#standalone)
  - [Fluent Builder](#fluent-builder)
  - [From Configuration](#from-configuration)
  - [Multiple Named Clients](#multiple-named-clients)
  - [Resilience and the HTTP Pipeline](#resilience-and-the-http-pipeline)
- [Configuration Options](#configuration-options)
- [The Result Pattern](#the-result-pattern)
- [Error Handling](#error-handling)
- [Identifiers](#identifiers)
- [Listings](#listings)
- [Content Items](#content-items)
- [Language Variants](#language-variants)
- [Strongly-Typed Models](#strongly-typed-models)
- [Publishing and Scheduling](#publishing-and-scheduling)
- [Assets](#assets)
- [Content Model](#content-model)
- [Workflows](#workflows)
- [Environment and Administration](#environment-and-administration)
- [Further Information](#further-information)
- [Contributing](#contributing)
- [License](#license)

## Installation

Install the SDK via the NuGet Package Manager. The modernized API documented here ships as a **prerelease** during the beta, so include the `--prerelease` flag — without it you get the previous stable API, which these examples do not match:

```bash
dotnet add package Kontent.Ai.Management --prerelease
```

The SDK targets `net10.0`.

## Upgrade Guide

If you are moving from the stable `8.x` SDK to the modernized prerelease, read the [upgrade guide](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/upgrade-guide.md). It covers the result pattern, listing changes, strongly-typed model changes, `System.Text.Json`, and removed legacy surfaces.

## Quick Start

The fastest path to a first call — a standalone client, ideal for scripts and simple apps. For applications, prefer [dependency injection](#with-dependency-injection).

```csharp
using Kontent.Ai.Management;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Models.Shared;

await using var client = new ManagementClient(new ManagementOptions
{
    EnvironmentId = "<YOUR_ENVIRONMENT_ID>",
    ApiKey = "<YOUR_API_KEY>"
});

var result = await client.GetContentItemAsync(Reference.ByCodename("on_roasts"));

if (result.IsSuccess)
{
    Console.WriteLine(result.Value.Name);
}
else
{
    Console.WriteLine($"{result.StatusCode}: {result.Error?.Message}");
}
```

> [!NOTE]
> The Management API must be activated for your environment, and you need a Management API key. See [Making requests](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/#section/Making-requests).

## Creating the Client

There are three entry points, in order of preference: **dependency injection** for applications, the **standalone constructor** for scripts and simple consumers, and the **fluent builder** when a non-DI consumer needs to customize the HTTP pipeline.

### With Dependency Injection

Register the client on your `IServiceCollection`:

```csharp
services.AddManagementClient(options =>
{
    options.EnvironmentId = "<YOUR_ENVIRONMENT_ID>";
    options.ApiKey = "<YOUR_API_KEY>";
});
```

`IManagementClient` is then resolvable from the container — inject it into your own services. This is the recommended approach: the container owns the client's lifetime and its underlying `HttpClient` pipeline (via `IHttpClientFactory`).

> [!NOTE]
> A DI-resolved client is owned by the container — do **not** dispose it yourself. Disposal is a no-op on DI-managed instances; the container releases the underlying HTTP resources.

### Standalone

For scripts and simple consumers, construct the client directly:

```csharp
await using var client = new ManagementClient(new ManagementOptions
{
    EnvironmentId = "<YOUR_ENVIRONMENT_ID>",
    ApiKey = "<YOUR_API_KEY>"
});
```

A standalone client owns its `HttpClient` instances — dispose it when you are done. `ManagementClientBuilder.Build()` and the `ManagementClient` constructor both hand back the concrete `ManagementClient`, which implements `IDisposable` and `IAsyncDisposable` (hence the `await using` above). The `IManagementClient` interface deliberately does not: a client resolved from a container is owned by the container, and nothing you inject should be disposed by you.

### Fluent Builder

When you are **not** using DI but still need to customize the resilience pipeline, use `ManagementClientBuilder`:

```csharp
await using var client = ManagementClientBuilder
    .WithOptions(options =>
    {
        options.EnvironmentId = "<YOUR_ENVIRONMENT_ID>";
        options.ApiKey = "<YOUR_API_KEY>";
    })
    .WithResilience(pipeline => /* customize the Polly pipeline */ pipeline.AddTimeout(TimeSpan.FromSeconds(30)))
    .Build();
```

The builder is a thin wrapper over the resource-owning constructor — it does not spin up a private service provider. The built client owns its HTTP resources, so dispose it as with the standalone constructor.

### From Configuration

Bind the options from an `IConfiguration` section — `ManagementOptions` by default:

```json
{
  "ManagementOptions": {
    "EnvironmentId": "<YOUR_ENVIRONMENT_ID>",
    "ApiKey": "<YOUR_API_KEY>"
  }
}
```

```csharp
services.AddManagementClient(configuration);
// or bind a differently-named section:
services.AddManagementClient(configuration, "MyManagementSection");
```

The configuration-based overloads accept the same optional `configureHttpClient` / `configureResilience` hooks as the action-based ones, and every form has a named counterpart:

```csharp
services.AddManagementClient("production", configuration, "Management:Production");
```

You can also hand over a pre-built options instance, or configure options from other registered services:

```csharp
services.AddManagementClient(new ManagementOptions
{
    EnvironmentId = "<YOUR_ENVIRONMENT_ID>",
    ApiKey = "<YOUR_API_KEY>",
});

services.AddManagementClient((sp, options) =>
{
    var secrets = sp.GetRequiredService<ISecretStore>();
    options.EnvironmentId = secrets.EnvironmentId;
    options.ApiKey = secrets.ManagementApiKey;
});
```

The instance's values are copied onto the options the container materializes — the object itself is not registered, so mutating it afterwards has no effect once the options have been read.

### Multiple Named Clients

Register more than one client by giving each a unique name:

```csharp
services.AddManagementClient("production", options =>
{
    options.EnvironmentId = "<PRODUCTION_ENVIRONMENT_ID>";
    options.ApiKey = "<PRODUCTION_API_KEY>";
});

services.AddManagementClient("staging", options =>
{
    options.EnvironmentId = "<STAGING_ENVIRONMENT_ID>";
    options.ApiKey = "<STAGING_API_KEY>";
});
```

Resolve a named client through `IManagementClientFactory`:

```csharp
public class ContentMigrator(IManagementClientFactory clientFactory)
{
    public async Task RunAsync()
    {
        var production = clientFactory.Get("production");
        var staging = clientFactory.Get("staging");
        // ...
    }
}
```

### Resilience and the HTTP Pipeline

Every client comes with a built-in resilience pipeline (powered by [`Microsoft.Extensions.Http.Resilience`](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)): retries on transient failures and `429` responses, exponential backoff with jitter, and `Retry-After` handling. Set `EnableResilience = false` to turn it into a passthrough.

To replace the pipeline wholesale, use the `configureResilience` hook on the DI overload, or `WithResilience(...)` on the builder:

```csharp
services.AddManagementClient(
    options => { options.EnvironmentId = "..."; options.ApiKey = "..."; },
    configureHttpClient: null,
    configureResilience: pipeline => pipeline
        .AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 5 })
        .AddTimeout(TimeSpan.FromSeconds(30)));
```

> [!NOTE]
> Unlike the sibling Delivery and Sync SDKs, the Management pipeline has **no default per-attempt timeout** — asset and file uploads can legitimately run long, and a blind retry would just re-upload. Add one via the hooks above if you need it. The ceiling on the call as a whole is `Timeout`, which defaults to 30 minutes and covers every attempt plus the waits between them — enough to carry a maximum-size (2 GB) asset over roughly a 10 Mbps link. Raise it for slower links, or lower it if you would rather fail fast.

#### Uploading a file

The upload endpoint needs the file's size up front, so `FileContentSource` accepts only sources that can report one: a `byte[]`, a file path, or a **seekable** stream. Passing a non-seekable stream throws — buffer it first. This also makes every upload safe to retry: `byte[]` and file-path sources reopen on each attempt, and a seekable stream is rewound, so a `429` retry re-sends the same bytes rather than a truncated body.

## Configuration Options

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `EnvironmentId` | For environment endpoints | — | The GUID of your Kontent.ai environment. Required for everything except subscription-scoped endpoints. |
| `ApiKey` | Yes | — | A Management API key for environment-scoped endpoints, or a **Subscription API key** for subscription-scoped ones. They are different keys — see the note on subscription endpoints below. |
| `SubscriptionId` | For subscription endpoints | — | The subscription GUID. Required only for subscription-scoped endpoints (such as user management). |
| `EnableResilience` | No | `true` | Toggles the built-in retry/backoff pipeline without uninstalling it. |
| `Timeout` | No | `30 minutes` | Ceiling on one call, covering every retry attempt and the waits between them. Sized against the 2 GB asset limit. Use `Timeout.InfiniteTimeSpan` to be bounded only by your `CancellationToken`. |
| `Endpoint` | No | `https://manage.kontent.ai` | The Management API base address; the SDK appends the versioned, scoped path. Override only when targeting a non-production endpoint. |

`ManagementOptions` validates on use: a missing `ApiKey`, a malformed identifier, or configuring **neither** `EnvironmentId` nor `SubscriptionId` surfaces as a `ValidationException` from the constructor/builder, or an `OptionsValidationException` when DI options validation runs during host startup.

Configure the scope you actually call. Each is built only when its identifier is present, so a client scoped to one and used for the other fails immediately, naming the missing option, rather than sending a request to a path with an empty segment:

```
EnvironmentId is not configured. Set ManagementOptions.EnvironmentId to call environment endpoints.
SubscriptionId is not configured. Set ManagementOptions.SubscriptionId to call subscription endpoints.
```

## The Result Pattern

Every `IManagementClient` method returns an `IManagementResult` (for void operations) or an `IManagementResult<T>` (for operations that yield a value). The SDK **does not throw** when a call fails — inspect the result instead. That covers Management API `4xx`/`5xx` responses, a transport failure that never reached the server, and a response whose body could not be read; where an exception caused the failure, `Error.Exception` carries it.

> [!IMPORTANT]
> Do not write `try`/`catch` around a call expecting to catch a failed request — it will not fire. `IsSuccess` is the check.

```csharp
var result = await client.CreateContentItemAsync(new ContentItemCreateModel
{
    Name = "On Roasts",
    Codename = "on_roasts",
    Type = Reference.ByCodename("article")
});

if (result.IsSuccess)
{
    ContentItemModel item = result.Value;
}
```

A result carries:

- `IsSuccess` — whether the operation succeeded.
- `Value` — the returned value, on success (`IManagementResult<T>` only).
- `Error` — the failure detail, on failure (see [Error Handling](#error-handling)).
- `StatusCode`, `RequestUrl` — response diagnostics from the HTTP response.

For call sites that would rather not branch, two opt-in conveniences live on the result:

```csharp
// Throw on failure (the thrown ManagementException carries the IError) and get the value:
var item = (await client.GetContentItemAsync(Reference.ByCodename("on_roasts"))).EnsureSuccess();

// Or the Try pattern:
if ((await client.GetContentItemAsync(Reference.ByCodename("on_roasts"))).TryGetValue(out var value))
{
    Console.WriteLine(value.Name);
}
```

The SDK itself never throws `ManagementException` — it surfaces only when you opt in with `EnsureSuccess()`.

When composing your own multi-step helpers in the same style as the SDK's (upload → create → link), `AsFailure<T>()` re-projects a failed result onto the helper's return type, preserving the error, status code, and request URL:

```csharp
async Task<IManagementResult<AssetModel>> UploadAndCreateAsync(IManagementClient client, FileContentSource file)
{
    var upload = await client.UploadFileAsync(file);
    if (!upload.IsSuccess)
    {
        return upload.AsFailure<AssetModel>();   // propagate the first failure
    }

    return await client.CreateAssetAsync(new AssetCreateModel { FileReference = upload.Value });
}
```

## Error Handling

On failure, `result.Error` (an `IError`) describes what went wrong:

```csharp
var result = await client.CreateContentItemAsync(model);

if (!result.IsSuccess)
{
    Console.WriteLine($"Request failed ({result.StatusCode}): {result.Error?.Message}");
    Console.WriteLine($"Request ID: {result.Error?.RequestId}");   // quote this when reporting an issue

    foreach (var validationError in result.Error?.ValidationErrors ?? [])
    {
        Console.WriteLine(validationError.Message);
    }

    return;
}
```

`IError` exposes `Message`, `RequestId`, `ErrorCode` (Kontent.ai's diagnostic code, not the HTTP status), `ValidationErrors`, and the underlying `Exception` when a response could not be parsed as a standard Management API error envelope.

When you need to branch on a specific failure, compare `ErrorCode` against the `ManagementErrorCodes` catalog rather than a magic number:

```csharp
var result = await client.UpsertLanguageVariantAsync(identifier, article);

if (!result.IsSuccess && result.Error?.ErrorCode == ManagementErrorCodes.PublishedOrScheduledVariantCannotBeUpdated)
{
    // The variant is published; create a new version, then retry the edit.
    await client.CreateNewVersionOfLanguageVariantAsync(identifier);
    result = await client.UpsertLanguageVariantAsync(identifier, article);
}
```

`ManagementErrorCodes` is a curated set of the codes callers commonly act on — variant workflow-state conflicts, duplicate external IDs, concurrency, and rate limits. The codes are not unique (the API reuses some across unrelated conditions), so inspect `Message` as well when the distinction matters.

> [!IMPORTANT]
> A failed call is a result, not an exception. A `404`, an API validation rejection, an unreachable host and an unreadable response body all come back as `IsSuccess == false`.
>
> Four things still throw:
>
> - **Cancellation** — a cancelled call throws `OperationCanceledException`, so `Task.IsCanceled` and cancellation handlers behave normally. (An expired timeout is *not* cancellation: the request was sent and may have been applied, so it comes back as a failed result.)
> - **Programmer errors** — a `null` argument throws `ArgumentNullException`.
> - **Invalid configuration** — validated when the client is built or registered.
> - **`EnsureSuccess()`** — the opt-in conversion of a failed result into a `ManagementException`.
>
> The strongly-typed language-variant overloads add one more: projecting a response onto a generated record that no longer matches the content type throws, because that is a mismatch between your model and the environment rather than an outcome of the call.

## Identifiers

Most operations target an entity through a `Reference`, which can be built from a codename, an internal ID, or an external ID:

```csharp
var byCodename = Reference.ByCodename("on_roasts");
var byId = Reference.ById(Guid.Parse("9539c671-d578-4fd3-aa5c-b2d8e486c9b8"));
var byExternalId = Reference.ByExternalId("Ext-Item-456-Brno");
```

User-management endpoints use a `UserIdentifier`:

```csharp
var byEmail = UserIdentifier.ByEmail("user@example.com");
var byUserId = UserIdentifier.ById("usr_0vKjTCH2TkO687K3y3bKNS");
```

A language variant is identified by the pairing of its content item and its language. The `ByCodenames` / `ByIds` / `ByExternalIds` factories cover the common case; the constructor takes any two `Reference`s when you need to mix kinds:

```csharp
var variantIdentifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

// mix identifier kinds via the constructor:
var mixed = new LanguageVariantIdentifier(Reference.ById(itemId), Reference.ByCodename("en-US"));
```

Objects you have already fetched convert straight back into identifiers — `ToReference()` on the models you list and act on (`ContentItemModel`, `AssetModel`, `ContentTypeModel`, `LanguageModel`, …), and `ToIdentifier()` on a fetched variant or an items-with-variants filter result:

```csharp
await client.DeleteContentItemAsync(item.ToReference());

// find variants, then act on them — no manual (item, language) reassembly:
foreach (var found in filterResult.Value)
{
    await client.PublishLanguageVariantAsync(found.ToIdentifier());
}
```

Both reference by **id**, which is environment-specific — scripts that target a different environment should build the reference explicitly (`Reference.ByCodename(...)`).

> [!NOTE]
> Not every endpoint accepts every identifier kind — some are ID-only, some forbid external IDs. Passing an unsupported kind throws an `InvalidOperationException` before any request is sent.

## Listings

Paged listing endpoints return the whole set in one result — typically `Task<IManagementResult<IReadOnlyList<T>>>`. Continuation-token paging is handled internally: the SDK walks every page and merges them, so you never deal with pages yourself.

```csharp
var result = await client.ListContentItemsAsync();

if (!result.IsSuccess)
{
    Console.WriteLine($"Failed to list content items: {result.Error?.Message}");
    return;
}

foreach (var item in result.Value)   // result.Value is IReadOnlyList<ContentItemModel>
{
    Console.WriteLine(item.Name);
}
```

A listing is **all-or-nothing**: if any page fails, that first failure short-circuits and is returned as the result, so you never receive a silently truncated set.

> [!NOTE]
> Because every page is fetched and buffered before the result returns, a listing materializes the full set in memory. For the Management API's configuration data (types, languages, taxonomies, …) that's a non-issue.

### Walking large listings a page at a time

The endpoints whose results can grow large — content items, assets, the items-with-variants filter and bulk-get, the language-variant listings by type, collection, and space (which scale as items × languages), and an async validation task's issues — also expose a `ListXPageAsync` overload that fetches exactly one continuation-token page, so you can process the listing without buffering it all in memory (and stop whenever you like). It returns a `ListingPage<T>`: the page's `Items`, and the `ContinuationToken` you pass back for the next page. A `null` token means that was the last one.

```csharp
string? continuationToken = null;

do
{
    var result = await client.ListContentItemsPageAsync(continuationToken);
    if (!result.IsSuccess)
    {
        Console.WriteLine($"A page failed: {result.Error?.Message}");
        break;
    }

    foreach (var item in result.Value.Items)   // one page's worth
    {
        Console.WriteLine(item.Name);
    }

    continuationToken = result.Value.ContinuationToken;
}
while (continuationToken is not null);
```

Each call is one HTTP request and one ordinary result, so a failed page is handled exactly like a failure anywhere else in the SDK — including `EnsureSuccess()` if you would rather it throw.

Because the token is yours, an interrupted walk can be **resumed** rather than restarted — which matters most under a [rate limit](https://kontent.ai/learn/docs/apis/management-api-v2/api-limitations), since a page-per-request walk is exactly the workload that reaches the per-minute one. The resilience pipeline retries a `429` three times with backoff, but a sustained limit outlives that and surfaces as a failed result. Holding the last successful page's token makes recovery a single request:

```csharp
var page = (await client.ListContentItemsPageAsync(lastGoodToken)).EnsureSuccess();

foreach (var item in page.Items)
{
    Console.WriteLine(item.Name);
}

lastGoodToken = page.ContinuationToken;
```

Restarting the walk instead would re-request every page you already had — more traffic against the limit that just stopped you.

> [!NOTE]
> The token is opaque and server-issued; how long it stays valid is the API's contract, not the SDK's. It is dependable across a backoff. Verify before relying on one across a long pause or a process restart.

Reach for this only when a listing is genuinely large; everywhere else, `ListXAsync` is simpler.

## Content Items

A content item is the language-agnostic wrapper; the actual content lives in its [language variants](#language-variants).

```csharp
// Get
var item = await client.GetContentItemAsync(Reference.ByCodename("on_roasts"));

// Create
var created = await client.CreateContentItemAsync(new ContentItemCreateModel
{
    Name = "On Roasts",
    Codename = "on_roasts",
    Type = Reference.ByCodename("article"),
    Collection = Reference.ByDefaultCodename()   // optional
});

// Create or update by external ID
var upserted = await client.UpsertContentItemAsync(
    Reference.ByExternalId("59713"),
    new ContentItemUpsertModel
    {
        Name = "On Roasts",
        Type = Reference.ByCodename("article")
    });

// Delete
await client.DeleteContentItemAsync(Reference.ByCodename("on_roasts"));
```

To create an item and set its first variant in one call, use the `CreateContentItemWithVariantAsync` extension (a `<T>` overload takes a strongly-typed model instead):

```csharp
using Kontent.Ai.Management.Extensions;

var result = await client.CreateContentItemWithVariantAsync(
    new ContentItemCreateModel { Name = "On Roasts", Type = Reference.ByCodename("article") },
    Reference.ByCodename("en-US"),
    new LanguageVariantUpsertModel { Elements = [/* … */] });
```

> [!NOTE]
> This is a two-call composite. On a partial failure — the item is created but the variant upsert fails — the item is left in place (no rollback) and the returned failure carries the variant call's detail. Set an `ExternalId` on the item so a retry reuses it rather than creating a duplicate.

## Language Variants

> [!TIP]
> The most type-safe way to author a variant is a **[strongly-typed model](#strongly-typed-models)** — `await client.UpsertLanguageVariantAsync(id, typedModel)`. When you have generated content-type records that's the recommended path; the typed element records shown here cover the same ground without a generator.

A language variant holds the actual content for one language of a content item. Set its elements with a typed record per element kind — each locates its target element by `codename`, `id`, or `external_id` and carries a value shaped for that kind; omitted elements are left unchanged:

```csharp
using Kontent.Ai.Management.Models.LanguageVariants.Elements;

var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

var result = await client.UpsertLanguageVariantAsync(identifier, new LanguageVariantUpsertModel
{
    Elements =
    [
        new TextElement { Element = Reference.ByCodename("title"), Value = "On Roasts" },
        new DateTimeElement { Element = Reference.ByCodename("post_date"), Value = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero) }
    ]
});
```

Retrieve a single variant, or list every variant of an item:

```csharp
var variant = await client.GetLanguageVariantAsync(identifier);

var allVariants = await client.ListLanguageVariantsByItemAsync(Reference.ByCodename("on_roasts"));
// allVariants.Value is an IReadOnlyList<LanguageVariantModel>
```

You can also enumerate variants across a whole collection, space, or content type — see the `ListLanguageVariantsByCollectionAsync`, `…BySpaceAsync`, and `…ByTypeAsync` methods.

> [!TIP]
> These typed element records are the type-safe way to author a variant without a generated content type. When you do have generated content-type records, pass one directly — see [strongly-typed models](#strongly-typed-models).

### Element kinds

There is one record per element kind — `TextElement`, `NumberElement`, `DateTimeElement`, `MultipleChoiceElement`, `AssetElement`, `LinkedItemsElement`, `TaxonomyElement`, `SubpagesElement`, `UrlSlugElement`, `CustomElement`, and `RichTextElement` — each pairing an `Element` reference with a value typed for that kind. Some carry more than a bare value:

```csharp
using Kontent.Ai.Management.Models.Content;            // UrlSlugMode
using Kontent.Ai.Management.Models.LanguageVariants.Elements;

Elements =
[
    new DateTimeElement
    {
        Element = Reference.ByCodename("post_date"),
        Value = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero),
        DisplayTimeZone = "Europe/Prague"
    },
    new UrlSlugElement { Element = Reference.ByCodename("slug"), Value = "on-roasts", Mode = UrlSlugMode.Custom }
]
```

For an element kind the SDK doesn't model — or to replay a variant you fetched as raw JSON — use `DynamicElement`, whose `Value` is written to the wire as-is:

```csharp
new DynamicElement { Element = Reference.ByCodename("widget"), Value = "<opaque payload>" }
```

## Strongly-Typed Models

Instead of anonymous element objects, you can work with strongly-typed records that mirror your content types. Pass a generated model directly to `UpsertLanguageVariantAsync` — only the properties you set are sent (partial update):

```csharp
var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

var article = new Article
{
    Title = "On Roasts",
    PublishingDate = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero)
};

var result = await client.UpsertLanguageVariantAsync(identifier, article);
```

Retrieve a variant the same way with the generic overload. The generic get and upsert return a `LanguageVariantModel<T>`: the strongly-typed `Elements` plus the variant metadata it shares with the untyped `LanguageVariantModel` — item, language, workflow, schedule, last-modified, due date, note, contributors (every property except `Elements`):

```csharp
var result = await client.GetLanguageVariantAsync<Article>(identifier);
LanguageVariantModel<Article> variant = result.Value;

Article elements = variant.Elements;   // the strongly-typed element values
// every other property is variant metadata, shared with the untyped LanguageVariantModel:
Reference item = variant.Item;
Reference language = variant.Language;
DateTime lastModified = variant.LastModified;
```

### Element value types

These value types are the strongly-typed-record counterpart to the raw [element kinds](#element-kinds), and the two are deliberately distinct rather than duplicated: a raw `*Element` (e.g. `CustomElement`) carries its own `Element` reference because it lives in the untyped `Elements[]` array, whereas a `*Value` (e.g. `CustomValue`) is *just* the value — on a generated record the property already identifies which element it is. Pick the family that matches your authoring path; you don't mix them.

Many elements map directly to a single value, with nothing carried beside it — `string` for text, `decimal?` for number, `IEnumerable<Reference>` for linked items, taxonomy, and subpages, and `IEnumerable<AssetReference>` for assets. The rest carry a companion field beside the value, so each uses a small record that pairs the two. Rich text is the canonical case — `RichTextValue` holds the HTML `Value` plus its inline `Components` (see [Rich text and inline components](#rich-text-and-inline-components)). Three more follow the same shape — date & time, URL slug, and custom:

```csharp
var article = new Article
{
    // Date & time — an instant plus an optional IANA zone for how the UI displays it
    PublishingDate = new DateTimeValue
    {
        Value = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero),
        DisplayTimeZone = "Europe/Prague"
    },

    // URL slug — the slug plus whether it is custom or regenerated from its source element
    Slug = new UrlSlugValue { Value = "on-roasts", Mode = UrlSlugMode.Custom },

    // Custom element — the opaque value plus the plaintext used for search and filtering
    Rating = new CustomValue { Value = "{\"stars\":5}", SearchableValue = "5 stars" }
};
```

Each wrapper has an implicit conversion for the common case, so `PublishingDate = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero)`, `Slug = "on-roasts"` (custom mode), and `Rating = "{\"stars\":5}"` all work when you don't need the extra field.

> [!IMPORTANT]
> A date & time value is stored as a **UTC instant**; `DisplayTimeZone` is only a hint for how the editor renders it and never changes the instant. The element accepts a `DateTimeOffset` (not a `DateTime`) so the moment is unambiguous — a bare `DateTime` would be resolved against the machine's local zone. Whatever offset you supply is normalized to UTC on the wire.

> [!TIP]
> You don't have to hand-write these models. The [**Kontent.ai model generator**](https://github.com/kontent-ai/dotnet/tree/main/src/model-generator) generates strongly-typed records from your content model. Management-model generation is currently in active development — watch the repository for its release.

### Rich text and inline components

Rich text is authored as an HTML **string**. The SDK intentionally has no structured rich-text model on the write path — in a write-primary SDK that would mean a heavyweight tree builder for little gain. What it *does* provide is `RichTextBuilder`, which removes the one genuinely error-prone part of hand-authoring rich text: keeping each inline `<object data-id="…">` placeholder in the HTML in sync with the matching entry in the `components` array.

You interpolate helper calls directly into the HTML string. The builder mints the shared GUID, emits the placeholder, and records the matching component — both sides stay consistent and you never handle the GUID yourself:

```csharp
using Kontent.Ai.Management.Models.Content;

var rt = new RichTextBuilder();

var content = rt.Build($"""
    <h1>On Roasts</h1>
    <p>See {rt.ItemLink(Reference.ByCodename("intro"), "the introduction")} first.</p>
    {rt.Component(new Callout { Type = [CalloutType.Warning] })}
    {rt.Asset(new AssetReference { Codename = "roasting_chart" })}
    """);

var article = new Article { Title = "On Roasts", Content = content };
await client.UpsertLanguageVariantAsync(identifier, article);
```

`Build` returns a `RichTextValue` — the verbatim HTML plus the recorded components — ready to assign to a generated model's rich-text property. The helpers:

| Helper | Emits | Use for |
|--------|-------|---------|
| `Component(IElementsModel item)` | `<object data-type="component" data-id="…">` **and** records the component object | Embedding a generated content-type record as an inline component |
| `LinkedItem(Reference)` | `<object data-type="item" data-…="…">` | Referencing an existing content item inline |
| `ItemLink(Reference, linkText)` | `<a data-item-…="…">…</a>` | A hyperlink to a content item (link text is HTML-encoded) |
| `Asset(AssetReference)` | `<figure data-asset-…="…">` | Embedding an asset |

`Component` accepts any generated record (it must carry `[KontentType]`). Interpolation evaluates left-to-right, so the order you call the helpers is the order the components are recorded. `Build` snapshots and resets the builder, so one instance can produce several elements in turn; builders nested inside a component's own rich-text body are independent of the outer one.

> [!NOTE]
> The HTML is passed through verbatim — the builder does not sanitize or validate the markup; it is intended for trusted, code-authored content such as migration scripts. Helper attribute values and link text are HTML-encoded.

## Publishing and Scheduling

```csharp
var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

// Publish now
await client.PublishLanguageVariantAsync(identifier);

// Schedule publishing
await client.SchedulePublishingOfLanguageVariantAsync(identifier, new ScheduleModel
{
    ScheduledTo = new DateTimeOffset(2038, 1, 19, 4, 14, 8, TimeSpan.Zero),
    DisplayTimeZone = "Europe/London"
});

// Unpublish, and create a new draft version of a published variant
await client.UnpublishLanguageVariantAsync(identifier);
await client.CreateNewVersionOfLanguageVariantAsync(identifier);

// Move a variant to a different workflow step
await client.ChangeLanguageVariantWorkflowAsync(identifier, new ChangeLanguageVariantWorkflowModel(
    workflow: Reference.ByDefaultCodename(),
    step: Reference.ByCodename("review")));
```

`SchedulePublishingAndUnpublishingOfLanguageVariantAsync` sets both ends of a publish window in one call.

## Assets

An asset is a binary file plus its metadata. Creating one is a two-step operation under the hood — upload the file, then create the asset that references it — but the `CreateAssetAsync(FileContentSource, Func<FileReference, AssetCreateModel>)` extension does both in a single call: it uploads the file and hands the resulting `FileReference` to your factory so you can build the asset around it.

```csharp
using Kontent.Ai.Management.Extensions;

var stream = new MemoryStream(Encoding.UTF8.GetBytes("Hello world"));

var result = await client.CreateAssetAsync(
    new FileContentSource(stream, "hello.txt", "text/plain"),
    fileReference => new AssetCreateModel
    {
        FileReference = fileReference,
        Title = "Hello",
        // optionally assign taxonomy terms defined on the environment's asset type
        Elements =
        [
            new AssetTaxonomyElement
            {
                Element = Reference.ByCodename("taxonomy-categories"),
                Value = [Reference.ByCodename("hello"), Reference.ByCodename("sdk")]
            }
        ]
    });
```

The matching `UpsertAssetAsync(identifier, FileContentSource, AssetUpsertModel)` overload does the same for create-or-update. If the file upload fails, no asset is created and the upload's failure is returned.

When you need finer control — reusing one uploaded file across several assets, or separating the upload from the create — call the two steps yourself:

```csharp
// 1. Upload the binary file
var fileReference = (await client.UploadFileAsync(new FileContentSource(stream, "hello.txt", "text/plain"))).EnsureSuccess();

// 2. Create the asset referencing it
var result = await client.CreateAssetAsync(new AssetCreateModel
{
    FileReference = fileReference,
    Title = "Hello"
});
```

Assets are enumerated with `ListAssetsAsync`, updated with `UpsertAssetAsync`, and deleted with `DeleteAssetAsync`. Renditions (`CreateAssetRenditionAsync`, `ListAssetRenditionsAsync`) and the asset-folder hierarchy (`GetAssetFoldersAsync`, `CreateAssetFoldersAsync`, `ModifyAssetFoldersAsync`) are managed through their own methods.

## Content Model

Content types, snippets, and taxonomy groups can all be created and modified through the SDK. Elements of a content type are described by `ElementMetadataBase` subtypes — one per element kind (`TextElementMetadataModel`, `RichTextElementMetadataModel`, `NumberElementMetadataModel`, `AssetElementMetadataModel`, and so on):

```csharp
var result = await client.CreateContentTypeAsync(new ContentTypeCreateModel
{
    Name = "Article",
    Codename = "article",
    Elements =
    [
        new TextElementMetadataModel
        {
            Name = "Title",
            Codename = "title",
            IsRequired = true,
            DefaultValue = new TextElementDefaultValueModel("Untitled article")
        },
        new RichTextElementMetadataModel
        {
            Name = "Body",
            Codename = "body",
            AllowedBlocks = [RichTextBlockType.Text, RichTextBlockType.Images],
            AllowedContentTypes = [Reference.ByCodename("callout")]
        }
    ]
});
```

A taxonomy group is created with its terms (terms nest recursively):

```csharp
await client.CreateTaxonomyGroupAsync(new TaxonomyGroupCreateModel
{
    Name = "Categories",
    Codename = "categories",
    Terms =
    [
        new TaxonomyTermCreateModel { Name = "Coffee", Codename = "coffee" },
        new TaxonomyTermCreateModel { Name = "Brewing", Codename = "brewing" }
    ]
});
```

Content type snippets work the same way via `CreateContentTypeSnippetAsync` (a `ContentTypeSnippetCreateModel` carries `Name`, `Codename`, and `Elements`).

### Editing types and snippets with patch operations

Existing types, snippets, and taxonomy groups are changed with a list of **patch operations** rather than a full replace. Content-type and snippet operations address their target — an element, an option, a content group, or a per-element property — through a JSON-Pointer `path`. Rather than hand-write that wire grammar, use the `ContentTypePatch` and `ContentTypeSnippetPatch` factories: each method bundles the path, the correctly-typed value, and the operation verb, and returns a `ContentModelOperationBaseModel` so the operations compose into one list you can mix freely.

```csharp
using Kontent.Ai.Management.Models.Types.Patch;
using Kontent.Ai.Management.Models.Types.Elements;

await client.ModifyContentTypeAsync(Reference.ByCodename("article"),
[
    // add a new element
    ContentTypePatch.AddElement(new TextElementMetadataModel { Name = "Subtitle", Codename = "subtitle" }),

    // replace a scalar property of an existing element
    ContentTypePatch.ReplaceGuidelines(Reference.ByCodename("body"), "Keep it under 300 words."),
    ContentTypePatch.ReplaceIsRequired(Reference.ByCodename("title"), true),

    // set a rich-text / linked-items element's allowed content types (whole set at once) …
    ContentTypePatch.ReplaceAllowedContentTypes(Reference.ByCodename("related"),
        [Reference.ByCodename("article"), Reference.ByCodename("blog_post")]),

    // … or toggle a single allowed rich-text block
    ContentTypePatch.RemoveAllowedBlock(Reference.ByCodename("body"), RichTextBlockType.Tables),

    // reorder, reassign to a content group, remove
    ContentTypePatch.MoveElementAfter(Reference.ByCodename("subtitle"), Reference.ByCodename("title")),
    ContentTypePatch.ReplaceContentGroup(Reference.ByCodename("title"), Reference.ByCodename("metadata")),
    ContentTypePatch.RemoveElement(Reference.ByCodename("legacy_field")),
]);
```

`ContentTypeSnippetPatch` mirrors the same surface for `ModifyContentTypeSnippetAsync`, minus the content-group operations (snippets have none). The factories are the discoverable way through a finite but irregular grammar — some collections (allowed content types / item-link types / elements) are set as a whole array, while rich-text blocks are added and removed one at a time — and the method names reflect which is which.

Two fallbacks cover anything the factories don't model:

- **Raw-path factories** — `AddIntoRaw`, `ReplaceRaw`, `RemoveRaw`, `MoveRawBefore` / `MoveRawAfter` take the `path` string directly, so a property the SDK has no named factory for — e.g. an element's `maximum_text_length` — is still reachable in the same fluent style: `ContentTypePatch.ReplaceRaw("/elements/codename:summary/maximum_text_length", new MaximumTextLengthModel { Value = 280, AppliesTo = TextLengthLimitType.Characters })`.
- **The operation records** — construct `ContentModelReplacePatchModel { Path = …, Value = … }` (and its add / move / remove siblings) by hand for full control.

Taxonomy groups, languages, spaces, and custom apps address their target by a typed property-name enum instead of a path string. Each has its own small factory class — `TaxonomyGroupPatch`, `LanguagePatch`, `SpacePatch`, `CustomAppPatch` — that names the property and takes the correctly-typed value:

```csharp
await client.ModifyLanguageAsync(Reference.ByCodename("de-DE"),
[
    LanguagePatch.Name("Deutsch"),
    LanguagePatch.FallbackLanguage(Reference.ByCodename("en-US")),
]);

await client.ModifyTaxonomyGroupAsync(Reference.ByCodename("categories"),
[
    TaxonomyGroupPatch.ReplaceName(Reference.ByCodename("coffee"), "Coffee beans"),
]);

await client.ModifySpaceAsync(Reference.ByCodename("marketing"), [SpacePatch.RootItem(null)]);   // null unsets

await client.ModifyCustomAppAsync(Reference.ByCodename("dashboard"),
[
    CustomAppPatch.ReplaceSourceUrl("https://example.org/app"),
    CustomAppPatch.AddAllowedRole(Reference.ById(roleId)),
]);
```

Operations that already carry typed values — the taxonomy `addInto` / `remove` / `move` term operations — are constructed directly as their operation models.

The full set of each resource is listed with `ListContentTypesAsync`, `ListContentTypeSnippetsAsync`, and `ListTaxonomyGroupsAsync`.

## Workflows

```csharp
// List
var workflows = await client.ListWorkflowsAsync();

// Create / update / delete
var created = await client.CreateWorkflowAsync(new WorkflowUpsertModel { /* steps, scopes … */ });
await client.UpdateWorkflowAsync(Reference.ByDefaultCodename(), updatedWorkflow);
await client.DeleteWorkflowAsync(Reference.ByCodename("editorial"));
```

## Environment and Administration

The client also covers environment-level configuration and administration. These follow the same result-pattern and identifier conventions as the sections above; the full request/response shapes are in the [Management API reference](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/).

| Area | Key methods |
|------|-------------|
| **Languages** | `GetLanguageAsync`, `CreateLanguageAsync`, `ModifyLanguageAsync`, `ListLanguagesAsync` |
| **Collections** | `GetCollectionsAsync`, `ModifyCollectionsAsync` |
| **Spaces** | `ListSpacesAsync`, `GetSpaceAsync`, `CreateSpaceAsync`, `ModifySpaceAsync`, `DeleteSpaceAsync` |
| **Webhooks** | `ListWebhooksAsync`, `GetWebhookAsync`, `CreateWebhookAsync`, `EnableWebhookAsync`, `DisableWebhookAsync`, `DeleteWebhookAsync` |
| **Preview** | `GetPreviewConfigurationAsync`, `UpdatePreviewConfigurationAsync` |
| **Custom apps** | `ListCustomAppsAsync`, `GetCustomAppAsync`, `CreateCustomAppAsync`, `ModifyCustomAppAsync`, `DeleteCustomAppAsync` |
| **Roles** | `ListEnvironmentRolesAsync`, `GetEnvironmentRoleAsync` |
| **Environment users** | `InviteUserIntoEnvironmentAsync`, `UpdateUserRolesAsync` |
| **Environment lifecycle** | `GetEnvironmentInformationAsync`, `CloneEnvironmentAsync`, `GetEnvironmentCloningStateAsync`, `MarkEnvironmentAsProductionAsync`, `ModifyEnvironmentAsync`, `DeleteEnvironmentAsync` |
| **Validation** | `ValidateEnvironmentAsync`, `InitiateEnvironmentAsyncValidationTaskAsync`, `GetAsyncValidationTaskAsync`, `ListAsyncValidationTaskIssuesAsync` |

For example, creating a language:

```csharp
await client.CreateLanguageAsync(new LanguageCreateModel
{
    Name = "German",
    Codename = "de-DE",
    IsActive = true,
    FallbackLanguage = Reference.ByCodename("en-US")
});
```

> [!NOTE]
> Subscription-scoped endpoints — `ListSubscriptionProjectsAsync`, `ListSubscriptionUsersAsync`, `GetSubscriptionUserAsync`, `ActivateSubscriptionUserAsync`, `DeactivateSubscriptionUserAsync` — resolve against `/v2/subscriptions/{id}` instead of the environment, so they need `SubscriptionId` set. Calling one without it throws an `InvalidOperationException` naming the missing option.
>
> They also take a **Subscription API key**, not the Management API key an environment-scoped call uses. It is minted at `https://app.kontent.ai/subscription/<subscription-id>/api-keys` and only a subscription admin can create one; an environment's Management API key will not authenticate these endpoints.
>
> A client that only calls them needs no `EnvironmentId` — set one as well if the same client also calls environment endpoints.
>
> ```csharp
> var client = new ManagementClient(new ManagementOptions
> {
>     ApiKey = "<your Subscription API key>",
>     SubscriptionId = "<your subscription id>",
> });
>
> var projects = await client.ListSubscriptionProjectsAsync();
> ```

## Further Information

For migration details, see the [upgrade guide](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/upgrade-guide.md). For more developer resources, see the [Management API reference](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/) and the [.NET development overview](https://kontent.ai/learn/develop/develop-with-kontent-ai/net) on Kontent.ai Learn.

## Contributing

See the [contributing](https://github.com/kontent-ai/dotnet/blob/main/CONTRIBUTING.md) page for the best places to file issues, start discussions, and begin contributing.

## License

Distributed under the MIT License — see [`LICENSE.md`](https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md) for details.

[nuget-url]: https://www.nuget.org/packages/Kontent.Ai.Management
