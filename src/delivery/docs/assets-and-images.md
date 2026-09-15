# Assets and Images

Asset URLs the SDK hands you, and the three ways to change them: renditions configured in Kontent.ai,
a custom domain applied at mapping time, and on-the-fly transformations built with `ImageUrlBuilder`.

Registration and configuration live in the [README](../README.md); this guide assumes you have an
`IDeliveryClient`.

## Table of Contents

- [Asset Renditions](#asset-renditions)
- [Custom Asset Domain](#custom-asset-domain)
- [Image Transformation](#image-transformation)

## Asset Renditions

Assets can have pre-configured renditions (image presets) defined in Kontent.ai. Access these directly without applying additional transformations.

### Accessing Asset Renditions

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

### Default Rendition Preset

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

## Custom Asset Domain

If you serve assets through a custom CDN or domain (e.g. for branding, geo-routing, or security), the SDK can rewrite all asset URLs — including inline images in rich text — to use your domain while preserving the original path and query string.

### Configuration

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

## Image Transformation

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

### Available Transformations

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

### Available Formats

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
