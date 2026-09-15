# Kontent.ai model generator utility for .NET

[![Stable](https://img.shields.io/nuget/v/Kontent.Ai.ModelGenerator?style=for-the-badge&label=stable)](https://www.nuget.org/packages/Kontent.Ai.ModelGenerator)
[![Latest](https://img.shields.io/nuget/vpre/Kontent.Ai.ModelGenerator?style=for-the-badge&label=latest)](https://www.nuget.org/packages/Kontent.Ai.ModelGenerator/absoluteLatest)
[![Downloads](https://img.shields.io/nuget/dt/Kontent.Ai.ModelGenerator?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.ModelGenerator)

This utility generates strongly-typed **record-based models** for:

- the [Kontent.ai Delivery SDK for .NET (v19+)](https://github.com/kontent-ai/dotnet/tree/main/src/delivery) — default mode, for reading content
- the [Kontent.ai Management SDK for .NET](https://github.com/kontent-ai/dotnet/tree/main/src/management) — opt-in mode (`-m` / `--management`), for CRUD workflows.

> [!IMPORTANT]
> Three version requirements, and they are independent:
> - **The tool** needs the **.NET 10** runtime.
> - **Generated Delivery models** need `Kontent.Ai.Delivery` **19.0** or newer, and `--nullability semantic` needs **19.2.0** for `RichTextContent.Empty`.
> - **Generated Management models** need `Kontent.Ai.Management` **9.0** or newer.
>
> Your own project's target framework is not constrained by the tool — only by the SDK the models reference.
>
> If you need models for the legacy Delivery SDK (v18.x and earlier) or for Extended Delivery, use the [previous stable release](https://github.com/kontent-ai/model-generator-net/tree/9.0.0).

## Table of Contents

- [Installation & Usage](#installation--usage)
- [Upgrade Guide](#upgrade-guide)
- [Delivery Model Features](#delivery-model-features)
- [Generated Model Example (Delivery)](#generated-model-example-delivery)
- [Nullability mode](#nullability-mode)
- [Customizing Generated Models](#customizing-generated-models)
- [Management Models](#management-models)
- [Need Legacy Delivery SDK or Extended Delivery Support?](#need-legacy-delivery-sdk-or-extended-delivery-support)
- [Contributing](#contributing)
- [License](#license)

## Installation & Usage

### .NET Tool (Recommended)

The recommended way of obtaining this tool is installing it as a [.NET Tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools). You can install it as a global tool or per project as a local tool.

#### Global Tool

```bash
dotnet tool install -g Kontent.Ai.ModelGenerator
```

See the [changelog](https://github.com/kontent-ai/dotnet/blob/main/src/model-generator/CHANGELOG.md) for what each release changed.

Delivery models (default mode):

```bash
KontentModelGenerator --environmentId "<environmentId>" --namespace "MyProject.Models" --outputdir "./Models"
```

Management models (see [Management Models](#management-models)):

```bash
KontentModelGenerator --management --environmentId "<environmentId>" --apiKey "<management-api-key>" --outputdir "./Models"
```

Only `--environmentId` is required (plus `--apiKey` in Management mode); everything else has a default.
See [Parameters](#parameters) for the full set.

#### Local Tool

```bash
dotnet new tool-manifest
dotnet tool install Kontent.Ai.ModelGenerator
```

```bash
dotnet tool run KontentModelGenerator --environmentId "<environmentId>" --outputdir "./Models"
```

### Standalone apps for Windows, Linux, macOS

Releases ship as NuGet packages only. For a machine without .NET installed, build a
[self-contained app](https://docs.microsoft.com/en-us/dotnet/core/deploying/#publish-self-contained)
yourself:

```bash
git clone https://github.com/kontent-ai/dotnet.git
cd dotnet/src/model-generator/Kontent.Ai.ModelGenerator
dotnet publish -c release -r <RID> --self-contained true
```

`--self-contained true` is not optional: since .NET 8 a runtime identifier alone produces a
framework-dependent build, which still needs .NET 10 installed.

See the [list of all RIDs](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) for `<RID>`.

### Parameters

| Short key | Long key | Required | Default value | Description |
| --- | --- | :---: | --- | --- |
| `-i` | `--environmentId` | Yes | `null` | A GUID that can be found in [Kontent.ai](https://app.kontent.ai) -> Environment settings -> Environment ID |
| `-m` | `--management` | No | `false` | Switches the generator to **Management mode**. Emits models for the Management SDK instead of Delivery. See [Management Models](#management-models). |
| `-k` | `--apiKey` | Mgmt only | `null` | Management API key (required when `-m` / `--management` is used). |
| `-n` | `--namespace` | No | `KontentAiModels` | A name of the [C# namespace](https://msdn.microsoft.com/en-us/library/z2kcy19k.aspx) |
| `-o` | `--outputdir` | No | `./` | An output folder path |
| `-b`, `-r` | `--baseRecord` | No | `null` | If provided, a base record will be created and all generated records will derive from it via partial extender records |
| | `--nullability` | No | `strict` | Either `strict` or `semantic`. Delivery mode only. See [Nullability mode](#nullability-mode). |

A parameter that belongs to the mode you did not ask for is an error, not a silently ignored argument:
`-k` without `-m` fails rather than quietly generating Delivery models over your output directory, and
`--nullability` is refused with `-m` rather than accepted and ignored. This covers the section-qualified
form too, so `--ManagementOptions:ApiKey` without `-m` is rejected the same way `-k` is. Options supplied
through `appSettings.json` are not affected - a config file may carry both sections, and only the section
belonging to the mode you run is read.

### CLI Syntax

Short keys such as `-n "MyModels"` are interchangeable with the long keys `--namespace "MyModels"`, and `-n=MyModels` / `--namespace=MyModels` work too. Parameter **names** are case-insensitive, as are the values of `--nullability`; every other value — namespaces, paths, keys — is used exactly as written. To see all aspects of the syntax, see the [MS docs](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.commandlineconfigurationextensions.addcommandline).

### Config file

These parameters can also be set via an `appSettings.json` file in the directory you run the tool from.
Command-line parameters always take precedence. The file is not installed with the tool — copy
[the template](https://github.com/kontent-ai/dotnet/blob/main/src/model-generator/Kontent.Ai.ModelGenerator/appSettings.json)
into your working directory and edit it.

### Advanced configuration (Preview API, Secure API)

There are two ways of configuring advanced Delivery SDK options (such as secure API access, preview API access, and [others](https://github.com/kontent-ai/dotnet/blob/main/src/delivery/Kontent.Ai.Delivery.Abstractions/Configuration/DeliveryOptions.cs)):

1. Command-line arguments:
   ```bash
   --DeliveryOptions:UseSecureAccess true --DeliveryOptions:SecureAccessApiKey <SecuredApiKey>
   ```

2. An `appSettings.json` in the directory you run the tool from — see [Config file](#config-file)

## Upgrade Guide

Upgrade guides are kept one per major under [`docs/upgrade/`](https://github.com/kontent-ai/dotnet/tree/main/src/model-generator/docs/upgrade); skipping a major means reading them in sequence.

- Coming from **10.x** — read [10 → 11](https://github.com/kontent-ai/dotnet/blob/main/src/model-generator/docs/upgrade/10-to-11.md). The .NET 10 move and the removal of `--withtypeprovider` are the work; the generated code itself is unchanged.

## Delivery Model Features

The generated models use modern C# features and patterns:

- **Records** - Immutable `record` types with `{ get; init; }` accessors
- **Modern types** - `RichTextContent`, `Asset`, `TaxonomyTerm`, `IEmbeddedContent`
- **Partial records** - Easily extendable without modifying generated code
- **`ContentTypeCodename` attribute** - For source-generated TypeProvider discovery
- **`ContentTypeCodename` constant** - Access the content type codename at compile time (usable in `switch`/`case` labels, attribute arguments, and other contexts that require a compile-time constant) without reflection

> [!NOTE]
> If an element codename would produce a property or constant that collides with the built-in `ContentTypeCodename` constant (e.g., an element named `content_type_codename` or `content_type`), the element's member is automatically prefixed with an underscore (`_ContentTypeCodename`) to avoid conflicts. The `[JsonPropertyName]` attribute ensures deserialization still works correctly.

## Generated Model Example (Delivery)

For the Management-mode equivalent, jump to [Management Models](#management-models).

**Generated file: `Article.cs`**

```csharp
// <auto-generated>
// This code was generated by Kontent.ai model generator tool
// (see https://github.com/kontent-ai/dotnet/tree/main/src/model-generator).
//
// Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
// To extend this record, create a separate partial record with the same name.
// </auto-generated>

#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Attributes;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.RichText;
using Kontent.Ai.Delivery.SharedModels;

namespace KontentAiModels;

[ContentTypeCodename("article")]
public partial record Article
{
    public const string BodyCopyCodename = "body_copy";
    public const string CustomTrackingCodeCodename = "custom_tracking_code";
    public const string PersonasCodename = "personas";
    public const string PostDateCodename = "post_date";
    public const string RelatedArticlesCodename = "related_articles";
    public const string TeaserImageCodename = "teaser_image";
    public const string TitleCodename = "title";
    public const string UrlPatternCodename = "url_pattern";

    public const string ContentTypeCodename = "article";

    [JsonPropertyName("body_copy")]
    public RichTextContent? BodyCopy { get; init; }
    [JsonPropertyName("custom_tracking_code")]
    public string? CustomTrackingCode { get; init; }
    [JsonPropertyName("personas")]
    public IEnumerable<TaxonomyTerm>? Personas { get; init; }
    [JsonPropertyName("post_date")]
    public DateTimeContent? PostDate { get; init; }
    [JsonPropertyName("related_articles")]
    public IEnumerable<IEmbeddedContent>? RelatedArticles { get; init; }
    [JsonPropertyName("teaser_image")]
    public IEnumerable<Asset>? TeaserImage { get; init; }
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    [JsonPropertyName("url_pattern")]
    public string? UrlPattern { get; init; }
}
```

## Nullability mode

The generator supports two nullability strategies for element properties via the `--nullability` flag:

### `strict` (default)

Every element property is generated as a nullable type — `string?`, `RichTextContent?`, `IEnumerable<Asset>?`, etc. This is the conservative default. It's also useful if you use the Delivery SDK's [projection](https://kontent.ai/learn/docs/apis/openapi/delivery-api/#tag/Items-and-content-types/operation/list-content-items) features (`WithElements` / `WithoutElements`) and want the type system to distinguish "not fetched" (`null`) from "fetched and empty" — projected-away elements surface as `null` at runtime.

```csharp
public string? Title { get; init; }
public RichTextContent? BodyCopy { get; init; }
public IEnumerable<IEmbeddedContent>? RelatedArticles { get; init; }
```

### `semantic`

Element properties match the runtime semantics of the Delivery API: empty text, rich text and collection elements always come back populated, so they're generated as **non-nullable** with sensible default initializers. Numbers, dates and custom elements can be genuinely unset, so they remain nullable.

```csharp
public string Title { get; init; } = string.Empty;
public RichTextContent BodyCopy { get; init; } = RichTextContent.Empty;
public IEnumerable<IEmbeddedContent> RelatedArticles { get; init; } = [];
public double? Rating { get; init; }
public DateTimeContent? PostDate { get; init; }
public string? CustomTrackingCode { get; init; }
```

> [!NOTE]
> When combined with [projection](https://kontent.ai/learn/docs/apis/openapi/delivery-api/#tag/Items-and-content-types/operation/list-content-items) (`WithElements` / `WithoutElements`), an omitted element surfaces as the type's default (`""`, `[]`, `RichTextContent.Empty`) rather than `null` — so "not fetched" and "fetched and empty" look the same. That's fine if your code doesn't branch on that distinction; if it does, prefer `strict`.

> [!IMPORTANT]
> `--nullability semantic` requires Delivery SDK **19.2.0+** (for `RichTextContent.Empty`). It is planned to become the **default in a future major version** of the model generator.

## Customizing Generated Models

Since the generated models are **partial records**, you can extend them by creating your own partial record file:

**Generated file: `Article.cs` (auto-generated)**
```csharp
namespace KontentAiModels;

[ContentTypeCodename("article")]
public partial record Article
{
    public const string TitleCodename = "title";
    // ... other constants and properties

    public const string ContentTypeCodename = "article";

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}
```

**Your custom file: `Article.Custom.cs` (your customizations)**
```csharp
namespace KontentAiModels;

public partial record Article
{
    // Add computed properties
    public string Slug => Title?.ToLowerInvariant().Replace(" ", "-") ?? string.Empty;

    // Add custom methods
    public DateTime? PostedLocal() => PostDate is { Value: { } utc, DisplayTimezone: { } zone }
        ? TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(zone))
        : PostDate?.Value;

    // Add validation
    public bool IsValid() => !string.IsNullOrEmpty(Title) && BodyCopy != null;
}
```

The generator creates the base model, and you maintain customizations in separate files that won't be overwritten.

## Management Models

> [!IMPORTANT]
> The emitted code references types and attributes shipped by `Kontent.Ai.Management` — the
> `IElementsModel` marker, `[ContentType]`, `[ContentElement]`, `[ContentOption]`, and the value
> types `RichTextValue`, `AssetReference`, `Reference`, `UrlSlugValue`, `DateTimeValue` and
> `CustomValue`. Generated models therefore require **`Kontent.Ai.Management` 9.0 or newer**; they
> will not compile against 8.x. Keep the generator and the SDK on releases that shipped together —
> a single-option multiple-choice element generates a `{ContentType}{Element}?` property, and an
> SDK older than this generator rejects that property type when the record is first used.

When you need to **write** content to Kontent.ai (create / update / delete / publish via the Management API), pass `-m` / `--management` to switch the generator from Delivery mode into Management mode. The emitter produces strongly-typed records you can construct with object-initializer syntax and pass to `IManagementClient`.

### CLI

```bash
KontentModelGenerator --management \
    --environmentId "<environmentId>" \
    --apiKey "<management-api-key>" \
    [--namespace "<custom-namespace>"] \
    [--outputdir "<output-directory>"]
```

### What's different from Delivery models

| | Delivery | Management |
| --- | --- | --- |
| Use case | Read content, frontend rendering | CRUD via the Management API |
| Marker interface | None | `IElementsModel` (empty marker) |
| Element identity | `[JsonPropertyName("codename")]` | `[ContentElement(codename, id)]` — both required (codename for request serialization, ID for response deserialization) |
| Type-level metadata | `[ContentTypeCodename]` — emitted into your compilation, internal | `[ContentType(codename, id)]` — shipped by the SDK; the id is what typed reads match on |
| Collections | `IEnumerable<T>?` | `IEnumerable<T>?` |
| Element constraints | Implicit at API layer | **Not mirrored on the model.** Content-model rules (length, regex, allowed types, count limits, asset rules, ...) are enforced server-side by the Management API — the generated models carry identity only. |
| Multiple-choice | `IEnumerable<MultipleChoiceOption>?` | Per-element enum (`[ContentOption]` members); the property is `{ContentType}{Element}?` when the element allows one option and `IEnumerable<{ContentType}{Element}>?` when it allows several |
| Snippets | Implicit; values come back flattened | Flattened at generation time; properties carry `{snippet}__{element}` codenames |
| Required elements | Not exposed | **Not enforced on the model.** `is_required` is a publish-workflow gate in MAPI, not an upsert-shape constraint — every property stays nullable so partial draft saves work. |

### Generated model example

**Generated file: `Article.cs`**

```csharp
// <auto-generated/>

#nullable enable

using System;
using System.Collections.Generic;
using Kontent.Ai.Management;
using Kontent.Ai.Management.Annotations;
using Kontent.Ai.Management.Models.Content;
using Kontent.Ai.Management.Models.Shared;

namespace MyProject.Models;

[ContentType("article", "0ca9b0f8-...")]
public sealed partial record Article : IElementsModel
{
    [ContentElement("body", "7ed15846-...")]
    public RichTextValue? Body { get; init; }

    [ContentElement("category", "f6d310a3-...")]
    public IEnumerable<ArticleCategory>? Category { get; init; }

    [ContentElement("featured_image", "8d2c...")]
    public IEnumerable<AssetReference>? FeaturedImage { get; init; }

    [ContentElement("priority", "88ae3d9b-...")]
    public decimal? Priority { get; init; }

    [ContentElement("publish_at", "b12f0a44-...")]
    public DateTimeValue? PublishAt { get; init; }

    [ContentElement("rating_widget", "c93b71de-...")]
    public CustomValue? RatingWidget { get; init; }

    [ContentElement("related_teasers", "a3155ec4-...")]
    public IEnumerable<Reference>? RelatedTeasers { get; init; }

    [ContentElement("seo__meta_title", "09398b24-...")]
    public string? SeoMetaTitle { get; init; }

    [ContentElement("tags", "1314993e-...")]
    public IEnumerable<Reference>? Tags { get; init; }

    [ContentElement("title", "a47451eb-...")]
    public string? Title { get; init; }

    [ContentElement("url", "e5a8c0f1-...")]
    public UrlSlugValue? Url { get; init; }
}

public enum ArticleCategory
{
    [ContentOption("news", "d65a2212-...")] News,
    [ContentOption("release", "709b1208-...")] Release,
    [ContentOption("blog", "ae79c5a6-...")] Blog,
}
```

### Notes

- **Models are environment-specific** by virtue of their element IDs. Cloning an environment via data-ops produces logically identical content models with different element IDs — regenerate after cloning.
- **Snippets are flattened**. If your content type uses an `seo` snippet that contributes `meta_title` and `meta_description`, the generated record has `SeoMetaTitle` and `SeoMetaDescription` properties; the `[ContentElement]` attributes carry the `seo__meta_title` / `seo__meta_description` codenames the API expects.
- **Content-model constraints are not on the model.** Length, regex, allowed types, count limits, asset rules, and the like are enforced server-side by the Management API and surfaced via `IManagementResult` — the generated records carry element identity and value types only.

## Need Legacy Delivery SDK or Extended Delivery Support?

> [!NOTE]
> For these use cases, use the [previous stable release](https://github.com/kontent-ai/model-generator-net/tree/9.0.0):
>
> - **Legacy Delivery SDK (v18.x and earlier)** models
> - **Extended Delivery** models

## Contributing

Found a bug or have a feature request? [Open an issue](https://github.com/kontent-ai/dotnet/issues). Pull requests are welcome!

### Wall of Fame

We would like to express our thanks to the following people who contributed and made the project possible:

- Drazen Janjicek - [EXLRT](http://www.exlrt.com/)
- [Kashif Jamal Soofi](https://github.com/kashifsoofi)
- [Casey Brown](https://github.com/MajorGrits)

## License

Distributed under the MIT License — see [`LICENSE.md`](https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md) for details.
