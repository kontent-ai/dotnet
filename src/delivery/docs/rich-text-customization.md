# Rich Text Customization Guide

Rich text elements in Kontent.ai contain structured content that needs to be resolved into HTML for display. This guide covers all aspects of customizing rich text resolution, from basic link handling to complex asynchronous resolvers.

## Table of Contents

- [Overview](#overview)
- [Basic Rich Text Resolution](#basic-rich-text-resolution)
  - [Default Resolution](#default-resolution)
  - [Custom Resolution](#custom-resolution)
- [HTML Resolver Builder](#html-resolver-builder)
- [Content Item Link Resolvers](#content-item-link-resolvers)
  - [Global Link Resolver](#global-link-resolver)
  - [Type-Specific Link Resolvers](#type-specific-link-resolvers)
  - [URL Pattern Resolver](#url-pattern-resolver)
  - [Tuple-Based Link Resolvers](#tuple-based-link-resolvers)
  - [Advanced Link Resolution](#advanced-link-resolution)
- [Embedded Content Resolvers](#embedded-content-resolvers)
  - [Type-Safe Content Resolvers (Recommended)](#type-safe-content-resolvers-recommended)
  - [Pattern Matching with Embedded Content](#pattern-matching-with-embedded-content)
  - [Codename-Based Content Resolvers](#codename-based-content-resolvers)
  - [Async Content Resolvers](#async-content-resolvers)
  - [Nested Content Resolution](#nested-content-resolution)
  - [Tuple-Based Content Resolvers](#tuple-based-content-resolvers)
  - [Complex Component Example](#complex-component-example)
  - [Dynamic Mode Resolution](#dynamic-mode-resolution)
- [Failing Fast on a Missing Resolver](#failing-fast-on-a-missing-resolver)
- [Registering the Resolver with Dependency Injection](#registering-the-resolver-with-dependency-injection)
- [Rich Text Extension Methods](#rich-text-extension-methods)
  - [Available Extension Methods](#available-extension-methods)
  - [Examples](#examples)
- [Inline Image Resolvers](#inline-image-resolvers)
  - [Basic Image Resolution](#basic-image-resolution)
  - [Responsive Images](#responsive-images)
  - [Images with Captions](#images-with-captions)
- [Custom HTML Node Resolvers](#custom-html-node-resolvers)
  - [Element-Based Resolution](#element-based-resolution)
  - [Attribute-Based Resolution](#attribute-based-resolution)
- [Resolution Context](#resolution-context)
- [Best Practices](#best-practices)
  - [1. Use Type-Safe Resolvers](#1-use-type-safe-resolvers)
  - [2. Performance Optimization](#2-performance-optimization)
  - [2. Security](#2-security)
  - [3. Maintainability](#3-maintainability)
  - [4. Testability](#4-testability)
- [Troubleshooting](#troubleshooting)
  - [Content Not Rendering](#content-not-rendering)
  - [Links Not Working](#links-not-working)
  - [Deeply Nested HTML (Max Parsing Depth)](#deeply-nested-html-max-parsing-depth)
  - [Blocking on Resolution](#blocking-on-resolution)
  - [Performance Issues](#performance-issues)

## Overview

Rich text elements in Kontent.ai can contain:

- **Content item links**: Hyperlinks to other content items
- **Embedded content**: Components and linked items displayed inline
- **Inline images**: Images inserted within the text
- **Standard HTML**: Headings, paragraphs, lists, etc.

The SDK provides the `HtmlResolverBuilder` to customize how each of these elements is rendered.

## Basic Rich Text Resolution

### Default Resolution

The simplest way to render rich text:

```csharp
var result = await client.GetItem<Article>("my-article").ExecuteAsync();

if (result.IsSuccess)
{
    var article = result.Value;
    var html = await article.Elements.BodyCopy.ToHtmlAsync();
}
```

With no resolver registered, the defaults are:

| Block | Default |
|---|---|
| Text | HTML-encoded |
| HTML elements | passed through as authored |
| Inline images | an `<img>` with the asset URL, encoded |
| Content item links | a diagnostic HTML comment naming the content type and item id |
| Embedded content | a diagnostic HTML comment naming the type and codename |

A link and an embedded component have no sensible default — only your application knows the URL or the
markup — so they report themselves rather than disappearing. `ThrowOnMissingResolver()` turns those
comments into exceptions.

> [!IMPORTANT]
> **A resolver's return value is inserted as HTML, unescaped.** Three different rules apply to what you interpolate into it:
> - **Element values are text** — encode them (`HtmlEncoder.Default.Encode`), in element content and in attributes alike. An ampersand or a quote in ordinary copy breaks the markup even when nothing malicious is involved.
> - **URLs are attribute values** — encode them too.
> - **`resolveChildren(...)` output is already-rendered HTML** — do *not* encode it, or the children come out double-escaped.
>
> The SDK encodes what it renders itself (text nodes, default image URLs). Anything your resolver builds is yours to encode.

### Custom Resolution

Create a custom resolver for full control:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver("article", async (link, resolveChildren) =>
    {
        var inner = await resolveChildren(link.Children);
        return $"<a href=\"/articles/{link.Metadata?.UrlSlug}\">{inner}</a>";
    })
    .Build();

var html = await article.Elements.BodyCopy.ToHtmlAsync(resolver);
```

## HTML Resolver Builder

The `HtmlResolverBuilder` provides a fluent API for configuring resolution:

```csharp
var resolver = new HtmlResolverBuilder()
    // Content item link resolvers
    .WithContentItemLinkResolver(globalLinkResolver)
    .WithContentItemLinkResolver("article", articleLinkResolver)
    .WithContentItemLinkResolvers(linkResolverDictionary)

    // Embedded content resolvers
    .WithContentResolver("tweet", tweetResolver)
    .WithContentResolver("video", videoResolver)
    .WithContentResolvers(contentResolverDictionary)

    // Inline image resolver
    .WithInlineImageResolver(imageResolver)

    // Custom HTML node resolvers
    .WithHtmlNodeResolver("h1", h1Resolver)
    .WithHtmlNodeResolverForAttribute("data-custom", "value", customResolver)

    .Build();
```

## Content Item Link Resolvers

Content item links are hyperlinks in rich text that reference other content items.

### Global Link Resolver

A global resolver handles all content item links regardless of type:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver(async (link, resolveChildren) =>
    {
        // Fallback URL if metadata is not available
        var url = link.Metadata?.UrlSlug is { Length: > 0 }
            ? $"/content/{link.Metadata.UrlSlug}"
            : $"/content/{link.ItemId}";

        var inner = await resolveChildren(link.Children);
        return $"<a href=\"{url}\">{inner}</a>";
    })
    .Build();
```

**Link Properties:**

```csharp
public interface IContentItemLink : IBlockWithChildren, IRichTextBlock
{
    Guid ItemId { get; }                              // Referenced item's ID
    IContentLink Metadata { get; }                    // Link metadata (see below)
    IReadOnlyDictionary<string, string> Attributes { get; } // Anchor-tag attributes from rich text
    IReadOnlyList<IRichTextBlock> Children { get; }   // Inherited - the link text and any inline children
}

public interface IContentLink
{
    string Codename { get; }
    string ContentTypeCodename { get; }
    Guid Id { get; }
    string UrlSlug { get; }
}
```

> [!IMPORTANT]
> There is no `link.Text` property. Use `resolveChildren(link.Children)` from inside an `async` resolver to obtain the rendered inner HTML (the link text as authored, plus any inline formatting).

### Type-Specific Link Resolvers

Different content types often need different URL patterns:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver("article", async (link, resolveChildren) =>
    {
        var slug = link.Metadata?.UrlSlug ?? link.ItemId.ToString();
        var inner = await resolveChildren(link.Children);
        return $"<a href=\"/articles/{slug}\">{inner}</a>";
    })
    .WithContentItemLinkResolver("product", async (link, resolveChildren) =>
    {
        var slug = link.Metadata?.UrlSlug ?? link.ItemId.ToString();
        var inner = await resolveChildren(link.Children);
        return $"<a href=\"/shop/products/{slug}\">{inner}</a>";
    })
    .WithContentItemLinkResolver("author", async (link, resolveChildren) =>
    {
        var codename = link.Metadata?.Codename ?? link.ItemId.ToString();
        var inner = await resolveChildren(link.Children);
        return $"<a href=\"/about/team/{codename}\">{inner}</a>";
    })
    .Build();
```

**Priority**: Type-specific resolvers take precedence over global resolvers.

### URL Pattern Resolver

For simple routing patterns, use a URL pattern helper:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver(DefaultResolvers.UrlPatternResolver(
        new Dictionary<string, string>
        {
            ["article"] = "/articles/{urlslug}",
            ["blog_post"] = "/blog/{urlslug}",
            ["product"] = "/shop/products/{urlslug}",
            ["category"] = "/shop/categories/{codename}",
            ["author"] = "/about/team/{codename}"
        },
        fallbackPattern: "/content/{id}"))
    .Build();
```

**Pattern Placeholders** (substituted from `IContentItemLink.Metadata` / `ItemId`):
- `{codename}` — `Metadata.Codename`
- `{type}` — `Metadata.ContentTypeCodename`
- `{urlslug}` — `Metadata.UrlSlug`
- `{id}` — `ItemId.ToString()`

### Tuple-Based Link Resolvers

Pass multiple type-specific link resolvers using tuple overloads:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolvers(
        ("article", async (link, resolveChildren) =>
        {
            var inner = await resolveChildren(link.Children);
            return $"<a href=\"/articles/{link.Metadata?.UrlSlug}\">{inner}</a>";
        }),
        ("product", async (link, resolveChildren) =>
        {
            var inner = await resolveChildren(link.Children);
            return $"<a href=\"/shop/products/{link.Metadata?.UrlSlug}\">{inner}</a>";
        }),
        ("author", async (link, resolveChildren) =>
        {
            var inner = await resolveChildren(link.Children);
            return $"<a href=\"/about/team/{link.Metadata?.Codename}\">{inner}</a>";
        }))
    .Build();
```

### Advanced Link Resolution

Add custom attributes and classes:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver("article", async (link, resolveChildren) =>
    {
        var url = $"/articles/{link.Metadata?.UrlSlug}";
        var inner = await resolveChildren(link.Children);

        // Per-link styling can be driven from anchor-tag attributes (the rich-text editor's link options),
        // since IContentLink only exposes Codename / ContentTypeCodename / Id / UrlSlug. To branch on
        // element values of the linked item, look the item up in the response's ModularContent dictionary
        // (or via a typed lookup service).
        var cssClass = link.Attributes.TryGetValue("data-style", out var style) && style == "featured"
            ? "featured-link"
            : "standard-link";

        return $"<a href=\"{url}\" class=\"{cssClass}\" data-item-id=\"{link.ItemId}\">{inner}</a>";
    })
    .Build();
```

## Embedded Content Resolvers

Embedded content (formerly inline content items) are components displayed within rich text.

### Type-Safe Content Resolvers (Recommended)

The SDK supports strongly-typed embedded content resolvers that provide compile-time type safety and IntelliSense:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver<Tweet>(tweet =>
    {
        // Strongly-typed access to elements - no casting required!
        var tweetText = tweet.Elements.TweetText;
        var author = tweet.Elements.AuthorHandle;
        var tweetUrl = tweet.Elements.TweetUrl;

        return $@"
            <blockquote class=""twitter-tweet"">
                <p>{tweetText}</p>
                <cite>@{author}</cite>
                <a href=""{tweetUrl}"">View on Twitter</a>
            </blockquote>";
    })
    .WithContentResolver<Quote>(quote =>
    {
        var quoteText = quote.Elements.QuoteText;
        var attribution = quote.Elements.Attribution;

        return $@"
            <blockquote class=""pullquote"">
                <p>{quoteText}</p>
                {(attribution != null ? $"<cite>{attribution}</cite>" : "")}
            </blockquote>";
    })
    .Build();
```

**Benefits of Type-Safe Resolvers:**
- ✅ Compile-time type checking
- ✅ IntelliSense support for element properties
- ✅ Refactoring-friendly (rename detection)
- ✅ No runtime casting or null checks for element access

**Strongly-Typed Embedded Content Interface:**

```csharp
public interface IEmbeddedContent<out TModel> : IEmbeddedContent
{
    TModel Elements { get; }  // Strongly-typed elements
}
```

### Pattern Matching with Embedded Content

Use pattern matching to filter and process specific embedded content types:

```csharp
// Process rich text blocks with pattern matching
foreach (var block in article.Elements.BodyCopy)
{
    switch (block)
    {
        case IEmbeddedContent<Tweet> tweet:
            Console.WriteLine($"Found tweet by @{tweet.Elements.AuthorHandle}");
            break;

        case IEmbeddedContent<Video> video:
            Console.WriteLine($"Found video: {video.Elements.Title}");
            break;

        case IEmbeddedContent<Quote> quote:
            Console.WriteLine($"Found quote: {quote.Elements.QuoteText}");
            break;
    }
}

// Or use LINQ extension methods
var allTweets = article.Elements.BodyCopy
    .GetEmbeddedContent<Tweet>()
    .ToList();

var allQuoteTexts = article.Elements.BodyCopy
    .GetEmbeddedElements<Quote>()
    .Select(q => q.QuoteText)
    .ToList();
```

### Codename-Based Content Resolvers

For scenarios where you don't have strongly-typed models, you can still use codename-based resolvers:

A codename-keyed resolver receives the non-generic `IEmbeddedContent`, whose `Elements` is typed
`object` — so it cannot be indexed directly. When no model is registered for that content type the
item is an `IContentItem<IDynamicElements>`, a read-only dictionary of `JsonElement` keyed by element
codename. Pattern-match to it, then read the element's `value`:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver("tweet", content =>
    {
        if (content is not IContentItem<IDynamicElements> dynamic)
        {
            return string.Empty;
        }

        return $"""
            <blockquote class="twitter-tweet">
                <p>{Value(dynamic, "tweet_text")}</p>
                <cite>@{Value(dynamic, "author_handle")}</cite>
                <a href="{Value(dynamic, "tweet_url")}">View on Twitter</a>
            </blockquote>
            """;
    })
    .Build();

// Worth extracting once - every codename-keyed resolver needs it.
static string? Value(IContentItem<IDynamicElements> item, string codename)
    => item.Elements.TryGetValue(codename, out var element)
        ? element.GetProperty("value").GetString()
        : null;
```

> [!NOTE]
> Each `JsonElement` is the element's whole envelope — `{"type": …, "name": …, "value": …}` — which is why the value is read with `GetProperty("value")`. And the cast only succeeds when the type has **no** registered model: once one exists, the SDK hands the resolver `IEmbeddedContent<YourModel>` instead, and a typed resolver is the better tool.

**Resolver Priority:**
1. Type-based resolvers (highest priority)
2. Codename-based resolvers
3. Default/missing resolver handling

### Async Content Resolvers

Use async resolvers when you need to fetch additional data. Type-safe async resolvers provide the same benefits with `async`/`await`:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver<HostedVideo>(async video =>
    {
        var videoId = video.Elements.VideoId;

        // Fetch video metadata from external API with full type safety
        var videoData = await _videoService.GetVideoDataAsync(videoId);

        return $@"
            <div class=""video-embed"" data-video-id=""{videoId}"">
                <iframe src=""https://youtube.com/embed/{videoId}""
                        title=""{videoData.Title}""
                        width=""560"" height=""315"">
                </iframe>
                <p class=""video-caption"">{videoData.Description}</p>
            </div>";
    })
    .WithContentResolver<ProductShowcase>(async showcase =>
    {
        var productReference = showcase.Elements.ProductReference;

        // Fetch real-time product data
        var product = await _productService.GetProductAsync(productReference.Id);

        return $@"
            <div class=""product-card"">
                <img src=""{product.ImageUrl}"" alt=""{product.Name}"" />
                <h3>{product.Name}</h3>
                <p class=""price"">${product.CurrentPrice:F2}</p>
                <p class=""stock"">{product.StockStatus}</p>
                <a href=""/products/{product.Id}"">View Details</a>
            </div>";
    })
    .Build();
```

### Nested Content Resolution

Handle embedded content that itself contains rich text:

A rich-text property on a generated model is an `IRichTextContent`, so resolving it is the same
`ToHtmlAsync` call — the only wrinkle is handing the resolver to itself. Declare it first, assign it
second; the lambda captures the variable, not its value, so it sees the built resolver by the time it
runs:

```csharp
IHtmlResolver? resolver = null;

resolver = new HtmlResolverBuilder()
    .WithContentResolver<CalloutBox>(async callout =>
    {
        var bodyHtml = callout.Elements.Body is { } body
            ? await body.ToHtmlAsync(resolver)   // same resolver, so nesting continues to any depth
            : string.Empty;

        return $"""
            <div class="callout-box">
                <h4>{callout.Elements.Title}</h4>
                <div class="callout-body">{bodyHtml}</div>
            </div>
            """;
    })
    .Build();
```

> [!WARNING]
> Nesting is unbounded: a component that transitively contains itself will recurse until the stack runs out. If your content model allows that, track depth in a field the resolver closes over and stop at a limit.

### Tuple-Based Content Resolvers

Register multiple content resolvers using tuples for batch registration.

**Type-Safe Resolvers:**

Chain one `WithContentResolver<T>` per model type. Each names its type once and receives
`IEmbeddedContent<T>`, so there is no cast and no unreachable fallback branch to write.

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver<Tweet>(tweet =>
        $"<div class=\"twitter-embed\"><a href=\"{tweet.Elements.Url}\">View Tweet</a></div>")
    .WithContentResolver<Quote>(quote =>
        quote.Elements.Attribution != null
            ? $"<blockquote><p>{quote.Elements.QuoteText}</p><cite>{quote.Elements.Attribution}</cite></blockquote>"
            : $"<blockquote><p>{quote.Elements.QuoteText}</p></blockquote>")
    .WithContentResolver<CodeSnippet>(snippet =>
        $"<pre><code class=\"language-{snippet.Elements.Language}\">{System.Web.HttpUtility.HtmlEncode(snippet.Elements.Code)}</code></pre>")
    .Build();
```

**Resolvers for Model Types Known Only at Runtime:**

`WithContentResolvers` also accepts `Type` keys, for the case `WithContentResolver<T>` cannot serve —
model types discovered at runtime rather than named in source, such as models found by scanning an
assembly. The resolver is handed the non-generic `IEmbeddedContent`, because there is no type argument
to give it.

```csharp
var builder = new HtmlResolverBuilder();
foreach (var modelType in modelTypesFoundAtRuntime)
{
    builder.WithContentResolvers((modelType, content => $"<div>{content.System.Codename}</div>"));
}
var resolver = builder.Build();
```

**Codename-keyed tuples:**

```csharp
// Value(...) is the helper from Codename-Based Content Resolvers above.
var resolver = new HtmlResolverBuilder()
    .WithContentResolvers(
        ("tweet", content => content is IContentItem<IDynamicElements> t
            ? $"<div class=\"twitter-embed\"><a href=\"{Value(t, "url")}\">View Tweet</a></div>"
            : string.Empty),
        ("quote", content =>
        {
            if (content is not IContentItem<IDynamicElements> q) return string.Empty;

            var text = Value(q, "quote_text");
            var by = Value(q, "attribution");
            return by is null
                ? $"<blockquote><p>{text}</p></blockquote>"
                : $"<blockquote><p>{text}</p><cite>{by}</cite></blockquote>";
        }),
        ("code_snippet", content =>
        {
            if (content is not IContentItem<IDynamicElements> c) return string.Empty;

            var lang = Value(c, "language") ?? "plaintext";
            return $"<pre><code class=\"language-{lang}\">{HttpUtility.HtmlEncode(Value(c, "code"))}</code></pre>";
        })
    )
    .Build();
```

### Complex Component Example

A component whose own elements include linked items is the case where a typed resolver stops being a
preference and becomes the only reasonable option: in the dynamic form a linked-items element is a list
of *codenames*, and resolving them means a second lookup the resolver does not have. With a model, the
SDK has already hydrated them.

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentResolver<ImageGallery>(gallery =>
    {
        var figures = gallery.Elements.Images?
            .OfType<IEmbeddedContent<GalleryImage>>()
            .Select(image =>
            {
                var caption = image.Elements.Caption;
                var url = image.Elements.Image?.FirstOrDefault()?.Url;
                return $"""
                    <figure class="gallery-item">
                        <img src="{url}" alt="{caption}" />
                        {(caption is null ? "" : $"<figcaption>{caption}</figcaption>")}
                    </figure>
                    """;
            }) ?? [];

        return $"""<div class="image-gallery">{string.Concat(figures)}</div>""";
    })
    .Build();
```

### Dynamic Mode Resolution

A fully dynamic item gives you elements as raw `JsonElement`, so a rich text element is not parsed for
you. `ParseRichTextAsync` does it, and its second parameter is what resolves embedded content — pass the
response's `ModularContent` or the blocks come back with the text but none of the components.

```csharp
using System.Text.Json;
using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;

var result = await client.GetItems().ExecuteAsync(cancellationToken);
if (!result.IsSuccess) return;

foreach (var item in result.Value.Items.Cast<IContentItem<IDynamicElements>>())
{
    if (!item.Elements.TryGetValue("body_copy", out var bodyCopy)) continue;

    var richText = await bodyCopy.ParseRichTextAsync(result.Value.ModularContent, cancellationToken);
    var html = await richText!.ToHtmlAsync(resolver);
}
```

> [!WARNING]
> **`ModularContent` is exposed on listing and feed responses only.** A single-item `GetItem(...)` result does not carry it, so a rich text element read that way can be parsed but its embedded components cannot be resolved — `ParseRichTextAsync(element, null)` returns the text blocks and drops every component. Read through `GetItems()` when you need dynamic rich text with components, or generate a model for the type and let the SDK do it.

Embedded items in a dynamic response are `IEmbeddedContent<IDynamicElements>`, so register resolvers by
**codename** — `WithContentResolver<T>` never matches — and read values as shown in
[Codename-Based Content Resolvers](#codename-based-content-resolvers). `System` metadata is available on
every embedded item whatever its type:

```csharp
.WithContentResolver("any_type", content => $"""
    <div data-id="{content.System.Id}" data-type="{HtmlEncoder.Default.Encode(content.System.Type)}">
        {HtmlEncoder.Default.Encode(content.System.Name)}
    </div>
    """)
```

## Failing Fast on a Missing Resolver

By default an embedded type with no resolver renders as a diagnostic HTML comment, which keeps a page
rendering while telling you what was skipped. `ThrowOnMissingResolver()` turns that into an exception
instead — worth it where an unhandled type is a bug rather than a gap:

```csharp
var resolver = new HtmlResolverBuilder()
    .ThrowOnMissingResolver()
    .WithContentResolver<Tweet>(t => $"<blockquote>{t.Elements.TweetText}</blockquote>")
    .Build();
```

## Registering the Resolver with Dependency Injection

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

## Rich Text Extension Methods

The SDK provides extension methods on `IRichTextContent` for filtering and extracting specific block types. These are useful for processing rich text programmatically without rendering to HTML.

### Available Extension Methods

| Method | Description |
|--------|-------------|
| `GetBlocks<T>()` | Get all blocks of a specific type recursively |
| `GetContentItemLinks()` | Get all content item links |
| `GetInlineImages()` | Get all inline images |
| `GetEmbeddedContent()` | Get all embedded content items |
| `GetEmbeddedContent<T>()` | Get embedded content of a specific model type |
| `GetEmbeddedElements<T>()` | Get just the element models (unwrapped from IEmbeddedContent) |

### Examples

#### Get All Inline Images

```csharp
var article = result.Value.Elements;

// Extract all images for a gallery
var images = article.BodyCopy.GetInlineImages().ToList();

foreach (var image in images)
{
    Console.WriteLine($"Image: {image.Url}");
    Console.WriteLine($"  Description: {image.Description}");
    Console.WriteLine($"  Size: {image.Width}x{image.Height}");
}
```

#### Get All Content Item Links

```csharp
// Find all links to content items
var links = article.BodyCopy.GetContentItemLinks().ToList();

foreach (var link in links)
{
    Console.WriteLine($"Link to: {link.Metadata?.Codename}");
    Console.WriteLine($"  Type: {link.Metadata?.ContentTypeCodename}");
    Console.WriteLine($"  URL Slug: {link.Metadata?.UrlSlug}");
}
```

#### Get Embedded Content by Type

```csharp
// Get all tweets embedded in the article
var tweets = article.BodyCopy
    .GetEmbeddedContent<Tweet>()
    .ToList();

foreach (var tweet in tweets)
{
    Console.WriteLine($"Tweet by @{tweet.Elements.AuthorHandle}:");
    Console.WriteLine($"  {tweet.Elements.TweetText}");
}

// Get just the element models (without IEmbeddedContent wrapper)
var tweetElements = article.BodyCopy
    .GetEmbeddedElements<Tweet>()
    .ToList();

foreach (var tweetElement in tweetElements)
{
    Console.WriteLine($"Tweet: {tweetElement.TweetText}");
}
```

#### Get All Blocks of a Specific Type

```csharp
// Get all text nodes (for text analysis, word count, etc.)
var textNodes = article.BodyCopy
    .GetBlocks<ITextNode>()
    .ToList();

var wordCount = textNodes
    .Sum(t => t.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

Console.WriteLine($"Word count: {wordCount}");

// Get all HTML nodes with a specific tag
var headings = article.BodyCopy
    .GetBlocks<IHtmlNode>()
    .Where(n => n.TagName is "H1" or "H2" or "H3")
    .ToList();

Console.WriteLine("Headings in article:");
foreach (var heading in headings)
{
    // Get text content from children
    var text = string.Join("", heading.Children.OfType<ITextNode>().Select(t => t.Text));
    Console.WriteLine($"  {heading.TagName}: {text}");
}
```

#### Build a Table of Contents

```csharp
// Extract headings to build a table of contents
var tocEntries = article.BodyCopy
    .GetBlocks<IHtmlNode>()
    .Where(n => n.TagName.StartsWith("H", StringComparison.OrdinalIgnoreCase))
    .Select(h => new
    {
        Level = int.Parse(h.TagName[1..]),
        Text = string.Join("", h.Children.OfType<ITextNode>().Select(t => t.Text)),
        Id = h.Attributes.GetValueOrDefault("id")
    })
    .ToList();

foreach (var entry in tocEntries)
{
    var indent = new string(' ', (entry.Level - 1) * 2);
    Console.WriteLine($"{indent}- {entry.Text}");
}
```

## Inline Image Resolvers

Customize how images are rendered within rich text:

### Basic Image Resolution

```csharp
var resolver = new HtmlResolverBuilder()
    .WithInlineImageResolver((image, resolveChildren) =>
    {
        var url = image.Url;
        var description = image.Description ?? "Image";
        var width = image.Width;
        var height = image.Height;

        return ValueTask.FromResult(
            $"<img src=\"{url}\" alt=\"{description}\" width=\"{width}\" height=\"{height}\" />");
    })
    .Build();
```

**Image Properties:**

```csharp
public interface IInlineImage
{
    string Url { get; }
    string? Description { get; }
    int Width { get; }
    int Height { get; }
    Guid ImageId { get; }
}
```

### Responsive Images

`ImageUrlBuilder` composes the query correctly — appending `?w=` by hand corrupts a URL that already
carries a rendition or transformation:

```csharp
using System.Text.Encodings.Web;
using Kontent.Ai.Urls.ImageTransformation;

var resolver = new HtmlResolverBuilder()
    .WithInlineImageResolver((image, _) =>
    {
        string At(int width) => HtmlEncoder.Default.Encode(new ImageUrlBuilder(image.Url).WithWidth(width).Url.ToString());

        // Never advertise a width the source cannot supply - the CDN does not upscale, and a srcset
        // descriptor has to be the candidate's real width.
        var widths = new[] { 320, 640, 1024 }.Where(w => w <= image.Width);
        var srcset = string.Join(", ", widths.Select(w => $"{At(w)} {w}w"));

        return ValueTask.FromResult($"""
            <img src="{At(640)}"
                 srcset="{srcset}"
                 sizes="(max-width: 640px) 100vw, 640px"
                 alt="{HtmlEncoder.Default.Encode(image.Description ?? "")}"
                 loading="lazy" />
            """);
    })
    .Build();
```

> [!TIP]
> In Razor, [`Kontent.Ai.AspNetCore`](https://github.com/kontent-ai/dotnet/tree/main/src/aspnetcore)'s `<img-asset>` tag helper does all of this — width capping, rendition awareness and `srcset` generation — from configuration.

### Images with Captions

Wrap images in figure elements:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithInlineImageResolver((image, _) =>
    {
        var url = image.Url;
        var description = image.Description;

        var imgTag = $"<img src=\"{url}\" alt=\"{description ?? ""}\" loading=\"lazy\" />";

        return ValueTask.FromResult(
            description != null
                ? $"<figure><img src=\"{url}\" alt=\"{description}\" /><figcaption>{description}</figcaption></figure>"
                : imgTag);
    })
    .Build();
```

## Custom HTML Node Resolvers

Customize rendering of specific HTML elements:

### Element-Based Resolution

```csharp
var resolver = new HtmlResolverBuilder()
    .WithHtmlNodeResolver("h1", async (node, resolveChildren) =>
    {
        var content = await resolveChildren(node.Children);
        var id = SlugifyHelper.ToSlug(content); // your own helper

        return $"<h1 id=\"{id}\" class=\"page-heading\">{content}</h1>";
    })
    .WithHtmlNodeResolver("h2", async (node, resolveChildren) =>
    {
        var content = await resolveChildren(node.Children);
        return $"<h2 class=\"section-heading\">{content}</h2>";
    })
    .Build();
```

### Attribute-Based Resolution

`IHtmlNode.Attributes` is an `IReadOnlyDictionary<string, string>`. Use `TryGetValue` / `GetValueOrDefault` to read attribute values:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithHtmlNodeResolverForAttribute("data-component", "code-snippet", async (node, resolveChildren) =>
    {
        var code = await resolveChildren(node.Children);
        var language = node.Attributes.GetValueOrDefault("data-language") ?? "plaintext";

        return $@"
            <pre><code class=""language-{language}"">{code}</code></pre>";
    })
    .WithHtmlNodeResolverForAttribute("data-component", "alert", async (node, resolveChildren) =>
    {
        var content = await resolveChildren(node.Children);
        var type = node.Attributes.GetValueOrDefault("data-type") ?? "info";

        return $@"
            <div class=""alert alert-{type}"" role=""alert"">
                {content}
            </div>";
    })
    .Build();
```

## Resolution Context

`resolveChildren` is a `Func<IReadOnlyList<IRichTextBlock>, ValueTask<string>>`. Pass the block's `Children` collection (or any subset of blocks you want to render) to obtain the rendered HTML for them. Resolvers should be `async` whenever they call `resolveChildren`:

```csharp
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver(async (link, resolveChildren) =>
    {
        // Render the link's authored text (and any inline formatting)
        var linkContent = await resolveChildren(link.Children);

        var url = $"/content/{link.Metadata?.UrlSlug}";

        return $"<a href=\"{url}\" class=\"content-link\">{linkContent}</a>";
    })
    .WithHtmlNodeResolver("p", async (node, resolveChildren) =>
    {
        var content = await resolveChildren(node.Children);
        return $"<p>{content}</p>";
    })
    .Build();
```

> [!NOTE]
> `IHtmlNode` exposes only `TagName`, `Attributes`, and `Children` — there is no `PreviousSibling` / `NextSibling` / parent navigation. If you need positional context (e.g., "first paragraph in section"), apply CSS selectors like `:first-child` instead.

## Best Practices

### 1. Use Type-Safe Resolvers

**Prefer type-safe resolvers over codename-based resolvers:**

```csharp
// Preferred where a model exists: compile-time checked, no cast
.WithContentResolver<Quote>(quote =>
{
    return $"<blockquote>{quote.Elements.Text}</blockquote>";
})

// Codename-keyed: no compile-time check, and the cast below stops matching the moment the
// type gains a generated model. Correct for types you have no model for; second choice otherwise.
.WithContentResolver("quote", content => content is IContentItem<IDynamicElements> q
    ? $"<blockquote>{Value(q, "text")}</blockquote>"
    : string.Empty)
```

**Benefits:**
- Compile-time type safety prevents runtime errors
- IntelliSense support improves developer experience
- Refactoring tools work correctly with strongly-typed properties
- Better performance (no dictionary lookups for element access)

### 2. Performance Optimization

**Use Synchronous Resolvers When Possible:**

```csharp
// Good: Synchronous type-safe resolver
.WithContentResolver<Quote>(quote =>
{
    return $"<blockquote>{quote.Elements.Text}</blockquote>";
})

// Only use async when necessary
.WithContentResolver<ProductShowcase>(async showcase =>
{
    var data = await _externalService.GetDataAsync();  // Genuinely async
    return $"<div>{data}</div>";
})
```

**Cache Resolver Results:**

```csharp
private readonly IMemoryCache _cache;

.WithContentResolver("expensive_component", async content =>
{
    var cacheKey = $"component_{content.System.Id}";

    return await _cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        return await GenerateExpensiveHtmlAsync(content);
    });
})
```

### 2. Security

**Always HTML-Encode User Content:**

```csharp
using System.Web;

.WithContentResolver("user_comment", content =>
{
    if (content is not IContentItem<IDynamicElements> comment) return string.Empty;

    var safeText = HttpUtility.HtmlEncode(Value(comment, "comment"));

    return $"<div class=\"comment\">{safeText}</div>";
})
```

### 3. Maintainability

**Extract Resolvers to Methods:**

```csharp
public class RichTextResolvers
{
    // Type-safe resolver methods
    public static string ResolveTweet(IEmbeddedContent<Tweet> tweet)
    {
        var url = tweet.Elements.Url;
        return $"<blockquote class=\"twitter-tweet\"><a href=\"{url}\">Tweet</a></blockquote>";
    }

    public static string ResolveVideo(IEmbeddedContent<HostedVideo> video)
    {
        var videoId = video.Elements.VideoId;
        return $"<iframe src=\"https://youtube.com/embed/{videoId}\"></iframe>";
    }
}

// Usage with type-safe resolvers
var resolver = new HtmlResolverBuilder()
    .WithContentResolver<Tweet>(RichTextResolvers.ResolveTweet)
    .WithContentResolver<HostedVideo>(RichTextResolvers.ResolveVideo)
    .Build();
```

### 4. Testability

**Make Resolvers Testable:**

```csharp
public class HtmlResolverFactory
{
    private readonly IProductService _productService;
    private readonly IConfiguration _config;

    public HtmlResolverFactory(IProductService productService, IConfiguration config)
    {
        _productService = productService;
        _config = config;
    }

    public IHtmlResolver CreateResolver()
    {
        return new HtmlResolverBuilder()
            .WithContentResolver<ProductShowcase>(ResolveProductAsync)
            .Build();
    }

    // Type-safe testable method
    internal async Task<string> ResolveProductAsync(IEmbeddedContent<ProductShowcase> showcase)
    {
        var productId = Guid.Parse(showcase.Elements.Product.System.Id);
        var product = await _productService.GetProductAsync(productId);
        return $"<div>{product.Name}</div>";
    }
}

// In tests - easier to test with strongly-typed mocks
[Fact]
public async Task ResolveProductAsync_ReturnsCorrectHtml()
{
    var mockService = new Mock<IProductService>();
    var factory = new HtmlResolverFactory(mockService.Object, config);

    // Create strongly-typed test data
    var mockShowcase = CreateMockShowcase();

    var html = await factory.ResolveProductAsync(mockShowcase);

    Assert.Contains("Product Name", html);
}
```

## Troubleshooting

### Content Not Rendering

**Problem**: Embedded content appears as empty space.

**Solution**: Ensure you've registered a resolver for that content type:

```csharp
// Check what type is missing
var resolver = new HtmlResolverBuilder()
    .WithContentResolver("missing_type", content =>
    {
        // Temporary fallback to see what's missing
        return $"<!-- Missing resolver for: {content.System.Type} -->";
    })
    .Build();
```

### Links Not Working

**Problem**: Content item links render as plain text.

**Solution**: Register a link resolver:

```csharp
// At minimum, provide a global link resolver
var resolver = new HtmlResolverBuilder()
    .WithContentItemLinkResolver(async (link, resolveChildren) =>
    {
        var inner = await resolveChildren(link.Children);
        return $"<a href=\"/content/{link.ItemId}\">{inner}</a>";
    })
    .Build();
```

### Deeply Nested HTML (Max Parsing Depth)

**Problem**: Very deeply nested rich text HTML can cause excessive recursion or (in extreme cases) a stack overflow during parsing.

**Solution**: The SDK includes a max parsing depth guard in rich text processing. If you suspect deep nesting:

- Simplify the authored HTML structure (e.g., reduce deeply nested lists/tables)
- Prefer resolving/rendering strategies that avoid creating extremely deep node trees
- Enable Debug logging for `Kontent.Ai.Delivery` to see diagnostic messages when the max depth is exceeded

### Blocking on Resolution

**Problem**: `.Result` or `.GetAwaiter().GetResult()` on `ToHtmlAsync` or `ParseRichTextAsync`.

```csharp
var html = article.Elements.BodyCopy.ToHtmlAsync(resolver).Result;   // ❌
var html = await article.Elements.BodyCopy.ToHtmlAsync(resolver);    // ✅
```

Both return `ValueTask<T>`, and a `ValueTask` may be consumed once. Reading `.Result` before it has
completed is **undefined** by the BCL contract — not a slow-but-correct call. It may throw, or appear to
work for as long as the operation happens to finish synchronously, and break when it stops.

Blocking on a `Task` is separately bad: it holds a thread-pool thread, and because the pool injects new
threads slowly, enough blocked requests turn into a latency collapse. Every resolver registration has an
async overload, so there is never a reason to block inside one.

### Performance Issues

**Problem**: Rich text resolution is slow.

**Solutions**:

1. **Minimize async resolvers**
2. **Cache external data fetches**
3. **Use `ValueTask` for synchronous paths**
4. **Consider pre-rendering for static content**

---

**Related Documentation**:
- [Main README](../README.md)
- [Performance Optimization Guide](performance-optimization.md)
- [Extensibility Guide](extensibility-guide.md)
