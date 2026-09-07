# ASP.NET Core extensions for Kontent.ai apps

[![NuGet](https://img.shields.io/nuget/vpre/Kontent.Ai.AspNetCore?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.AspNetCore)
[![Downloads](https://img.shields.io/nuget/dt/Kontent.Ai.AspNetCore?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.AspNetCore)

Companion package to the [Kontent.ai Delivery SDK](https://github.com/kontent-ai/dotnet/tree/main/src/delivery) that provides ASP.NET Core–specific helpers: responsive image tag helpers, a rich-text tag helper that renders structured content via `IHtmlResolver`, and webhook signature validation middleware.

## Installation

```bash
dotnet add package Kontent.Ai.AspNetCore
```

The package targets `net10.0` and depends on `Kontent.Ai.Delivery` **20** or later. Its version is its own; see the [changelog](CHANGELOG.md) for what each release changed.

## Tag Helpers

### `img-asset` tag helper

Useful for rendering responsive images. Accepts any `IAsset` returned by the Delivery SDK (rich-text asset elements, asset element values, etc.).

`appsettings.json`:

```json
"ImageTransformationOptions": {
  "ResponsiveWidths": [ 200, 300, 400, 600, 800, 1000, 1200, 1400, 1600, 2000 ]
}
```

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ImageTransformationOptions>(
    builder.Configuration.GetSection(nameof(ImageTransformationOptions)));

var app = builder.Build();
```

`_ViewImports.cshtml`:

```razor
@addTagHelper *, Kontent.Ai.AspNetCore
```

`View.cshtml`:

```razor
<img-asset asset="@Model.TeaserImage.First()" class="img-responsive" default-width="300">
  <media-condition min-width="769" image-width="300" />
  <media-condition min-width="330" max-width="768" image-width="689" />
</img-asset>
```

Renders as:

```html
<img
  class="img-responsive"
  alt="Coffee Brewing Techniques"
  sizes="(min-width: 769px) 300px, (max-width: 768px) and (min-width: 330px) 689px, 300px"
  src="https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=1000"
  srcset="
    https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=200   200w,
    https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=300   300w,
    https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=400   400w,
    https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=600   600w,
    https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=800   800w,
    https://assets-us-01.kc-usercontent.com/975bf280-fd91-488c-994c-2f04416e5ee3/fcbb12e6-66a3-4672-85d9-d502d16b8d9c/which-brewing-fits-you-1080px.jpg?w=1000 1000w
  "
  title="Coffee Brewing Techniques"
/>
```

The source image is 1000 pixels wide, so the configured `1200`–`2000` widths collapse to `1000`: the CDN never upscales, a `srcset` width descriptor has to be the candidate's real width, and the tag helper caps candidates at `IAsset.Width` (or at the rendition's width when the URL already carries a rendition query, see [Renditions](#renditions)). Widths must be positive; the helper throws on a non-positive one, since a global misconfiguration should fail loudly rather than emit a broken `srcset` on every page.

#### Supported attributes

| Attribute | Purpose |
|---|---|
| `asset` | `IAsset` to render (required). |
| `title` | Overrides the `alt`/`title` attributes (defaults to `asset.Description`). |
| `default-width` | Width used as the last entry of the generated `sizes` attribute (default `300`). |
| `responsive-widths` | Per-tag override for the widths used to build `srcset` (falls back to `ImageTransformationOptions.ResponsiveWidths`). |
| `rendition` | Name of an asset rendition to apply. Today Kontent.ai supports only `default`. See [Renditions](#renditions) below. |
| `format` | Target image format (`jpg`, `png`, `png8`, `pjpg`, `gif`, `webp`). |
| `quality` | Compression quality for lossy formats (`1`–`100`). |
| `fit` | Fit transformation (`clip`, `scale`, `crop`). |
| `auto-format` | Enables WebP delivery when the browser advertises support. |
| `compression` | WebP compression (`lossless` / `lossy`); only meaningful when WebP is delivered. |

Standard HTML `width` and `height` attributes on the `<img-asset>` are honored and translate to `w=`/`h=` query parameters. Setting either one disables `srcset`/`sizes` generation.

#### Renditions

When `rendition="default"` is set and the asset exposes that rendition, its query string is appended to the asset URL to produce `src`, `srcset`/`sizes` are **not** generated (a rendition represents a single chosen crop), and only encoding-level transforms (`format`, `quality`, `auto-format`, `compression`) layer on top. The tag helper's `width`, `height`, and `fit` attributes are ignored because the rendition already defines those.

If the named rendition is not present on the asset, the tag helper silently falls back to the non-rendition path.

#### Attribute interaction matrix

| Configuration | `width` / `height` attrs | `ResponsiveWidths` → srcset | `fit` | Encoding (`format`, `quality`, `auto-format`, `compression`) |
|---|---|---|---|---|
| No `rendition` | Applied | Generates `srcset` + `sizes` | Applied | Applied to every generated URL |
| `rendition="default"` (found) | Ignored | Skipped | Ignored | Applied on top of the rendition query |
| `rendition="…"` (not found) | Applied | Generates `srcset` + `sizes` | Applied | Applied to every generated URL |

#### Custom asset domain

The Delivery SDK handles custom asset domains at mapping time — set `DeliveryOptions.CustomAssetDomain` to `"https://cdn.example.com"` where you configure the Delivery client. By the time assets reach the tag helper, their `Url` already points at the custom domain, so the `<img>` element emitted by `<img-asset>` uses that domain without any extra configuration in this package.

### `rich-text` tag helper

Renders Kontent.ai structured rich-text content as HTML in Razor views. Integrates with the Delivery SDK's `IHtmlResolver` for customizing how embedded content, content item links, inline images, and HTML nodes are rendered.

`Program.cs` (optional DI registration):

```csharp
using Kontent.Ai.AspNetCore.RichText;

builder.Services.AddKontentRichText(resolverBuilder => resolverBuilder
    .WithContentResolver<Article>(a =>
        $"<div class='article'><h2>{a.Elements.Title}</h2></div>")
    .WithContentItemLinkResolver("article", (link, _) =>
        ValueTask.FromResult($"<a href=\"/articles/{link.ItemId}\">link</a>")));
```

When the resolver configuration itself needs DI-resolved services (URL helpers, options, custom route resolvers, etc.), use the overload that exposes `IServiceProvider`:

```csharp
builder.Services.AddKontentRichText((sp, resolverBuilder) =>
{
    var routes = sp.GetRequiredService<IRouteResolver>();
    resolverBuilder.WithContentItemLinkResolver("article", (link, _) =>
        ValueTask.FromResult($"<a href=\"{routes.For(link.ItemId)}\">link</a>"));
});
```

The callback runs once, when the resolver is first resolved, with the **root** service provider: whatever it captures must be singleton-safe. A resolver that needs request-scoped services (an `IUrlHelper`, a per-request tenant) is registered by the application as a scoped `IHtmlResolver` instead, and the tag helper picks it up the same way.

`View.cshtml`:

```razor
@* Uses the IHtmlResolver registered in DI (or SDK defaults if none registered). *@
<rich-text content="@Model.Body" />

@* Per-view resolver override. *@
<rich-text content="@Model.Body" resolver="@myCustomResolver" />
```

The tag helper does not emit a `<rich-text>` wrapper — the resolver's HTML is rendered in place of the element.

#### Extension method alternative

For partial views, view components, or scenarios that benefit from an explicit `CancellationToken`, `ToHtmlContentAsync` is the Delivery SDK's `ToHtmlAsync` wrapped in an `IHtmlContent` so Razor does not encode it:

```razor
@inject IHtmlResolver Resolver
@await Model.Body.ToHtmlContentAsync(Resolver, ViewContext.HttpContext.RequestAborted)
```

An extension method cannot see the container, so without a `resolver` argument it uses the SDK's defaults, not the one registered with `AddKontentRichText`. Only the tag helper picks that one up on its own.

#### Without DI registration

Both the tag helper and the extension method fall back to `new HtmlResolverBuilder().Build()` when no resolver is provided and none is registered in DI. This uses the SDK's built-in defaults: HTML-encoded text nodes, default inline-image rendering, and diagnostic HTML comments for missing embedded-content and content-item-link resolvers. See the [Delivery SDK documentation](https://github.com/kontent-ai/dotnet/tree/main/src/delivery) for the full `IHtmlResolverBuilder` API.

## Webhooks

Three pieces, used together: middleware that rejects requests Kontent.ai did not sign, models for the
notification payload, and a mapper from a notification to the Delivery SDK's cache dependency keys.

### Signature validation middleware

Verifies the `X-Kontent-ai-Signature` header, falling back to the legacy `X-KC-Signature` header when the modern one is absent. A request carrying both is verified against the modern one. Returns `401 Unauthorized` when the signature is missing or invalid; the header is checked before the body is read, and the body stays readable for the endpoint.

`appsettings.json`:

```json
"WebhookOptions": {
  "Secret": "<your_secret>"
}
```

The secret is required. Without it no signature can be verified, so the host refuses to start rather than
letting unsigned requests through. Signatures are compared in constant time over the raw HMAC bytes.

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebhookSignatureValidator(
    context => context.Request.Path.StartsWithSegments("/webhooks", StringComparison.OrdinalIgnoreCase),
    builder.Configuration.GetSection(nameof(WebhookOptions)));
```

Other overloads take a `WebhookOptions` instance, an `Action<WebhookOptions>`, or nothing — in which case the options come from the container (`builder.Services.Configure<WebhookOptions>(…)`).

### Notification models

`WebhookNotification` binds the payload Kontent.ai sends: a batch of `Notifications`, each with `Data.System` (id, name, codename, last modified, and for content items the collection, workflow, step, language and type; for taxonomy terms the group) and `Message` (environment, object type, action, delivery slot, and for workflow-step changes the previous state). The members the API sends on every event are `required`; the rest are `null` when the event does not carry them. `WebhookObjectTypes`, `WebhookActions` and `WebhookDeliverySlots` hold the documented values.

### Cache invalidation

`GetCacheDependencyKeys()` maps a notification, or any subset of a batch, to the keys the Delivery SDK tags cached responses with, in the format `IDeliveryCacheManager` documents, so they are exactly the strings `InvalidateAsync` matches:

| Notification | Keys |
|---|---|
| `content_item` | `item_{codename}`, items-list scope |
| `content_type` | `type_{codename}`, types-list scope |
| `taxonomy` | `taxonomy_{group}`, taxonomies-list scope (the group is `TaxonomyGroup` for a term event, `Codename` for a group event) |
| `asset` | `asset_{id}` |
| `language` | nothing — see below |

A complete endpoint, with the Delivery client registered and a cache attached to it. `UseMemoryCache` comes
from `Kontent.Ai.Delivery.Caching`, which this package does not depend on — add it alongside:

```bash
dotnet add package Kontent.Ai.Delivery.Caching
```

```csharp
using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDeliveryClient(delivery =>
{
    delivery.Options.BindConfiguration("DeliveryOptions");
    delivery.UseMemoryCache(cache => cache.DefaultExpiration = TimeSpan.FromHours(1));
});
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(nameof(WebhookOptions)));

var app = builder.Build();

app.UseWebhookSignatureValidator(context => context.Request.Path.StartsWithSegments("/webhooks"));

app.MapPost("/webhooks/kontent", async (
    WebhookNotification notification,
    IDeliveryCacheManager cache,
    IOptions<DeliveryOptions> delivery,
    CancellationToken cancellationToken) =>
{
    // The keys carry no environment; only act on notifications for the environment this client reads.
    // Compared as GUIDs: the option is a string and validates in any GUID format, including upper case.
    var environmentId = Guid.Parse(delivery.Value.EnvironmentId);

    var relevant = notification.Notifications
        .Where(n => n.Message.EnvironmentId == environmentId)
        // A preview client bypasses the cache, so a content item change in the preview slot has nothing to
        // invalidate. Assets, types, taxonomies and languages are shared between the slots and always count.
        .Where(n => n.Message.ObjectType != WebhookObjectTypes.ContentItem || n.Message.DeliverySlot == WebhookDeliverySlots.Published)
        .ToList();

    // A language change can reach any cached response through fallbacks and has no key of its own.
    if (relevant.Any(n => n.Message.ObjectType == WebhookObjectTypes.Language) && cache is IDeliveryCachePurger purger)
    {
        await purger.PurgeAsync(cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    // InvalidateAsync reports failure instead of throwing (TTL is its backstop). A non-2xx makes Kontent.ai
    // resend the notification, so a failed invalidation gets a second chance rather than a 204.
    var invalidated = await cache.InvalidateAsync(relevant.GetCacheDependencyKeys(), cancellationToken);
    return invalidated ? Results.NoContent() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
```

The default client's `IDeliveryCacheManager` resolves unkeyed; a named client's is keyed by its name (`[FromKeyedServices("production")]`), and a client from `DeliveryClient.Create` exposes it as `CacheManager`. Respond with a `2xx` once the invalidation is done, and with anything else when it is not: any other status makes Kontent.ai retry the notification, with backoff, for up to three days, which is the retry a failed invalidation wants.

What invalidation does not cover:

- **Renames.** A notification carries only the new codename, so a response cached under the old key — the item itself, or a parent tagged with it — lives until it expires. The list scopes evict listings. An application that renames routinely purges instead.
- **Freshness after invalidation.** The Delivery CDN can serve the pre-change copy for a short while after the webhook arrives, and an ordinary read that follows caches whatever it gets. `.WaitForLoadingNewContent()` asks the API for the latest content; in this SDK that call bypasses the SDK cache, so it returns fresh content but does not warm the cache.
- **Purging is optional.** `IDeliveryCachePurger` is implemented by the SDK's own cache managers; a custom `IDeliveryCacheManager` may not implement it, which is why the sample pattern-matches.

## Upgrade Guide

- Coming from **0.x** — see the [0 → 1 upgrade guide](https://github.com/kontent-ai/dotnet/blob/main/src/aspnetcore/docs/upgrade/0-to-1.md). Guides are kept one per major under [`docs/upgrade/`](https://github.com/kontent-ai/dotnet/tree/main/src/aspnetcore/docs/upgrade).
