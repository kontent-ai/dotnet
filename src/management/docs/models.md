# Generated models and rich text

Authoring variants with generated content-type records instead of element records.

- [Generate Management models](#generate-management-models)
- [Read and write typed variants](#read-and-write-typed-variants)
- [Environment binding and partial updates](#environment-binding-and-partial-updates)
- [Element records and model values](#element-records-and-model-values)
- [Rich text and inline components](#rich-text-and-inline-components)

This guide is about **CLR records that mirror your content types**. Changing the content types
themselves — the CMS schema — is [content model](content-model.md).

## Generate Management models

The [model generator](https://github.com/kontent-ai/dotnet/tree/main/src/model-generator) emits records
from your existing content types; run it with `-m` / `--management`:

```sh
KontentModelGenerator --management --environmentId "<environmentId>" --apiKey "<management-api-key>" --outputdir "./Models"
```

The emitted records require `Kontent.Ai.Management` 9.0 or newer. Switches, output layout and the full
emitted shape are the generator's own documentation — see
[Management Models](https://github.com/kontent-ai/dotnet/tree/main/src/model-generator#management-models).

Extend generated records in separate partial record files so your additions survive regeneration.

Examples below assume a configured `IManagementClient client` (see
[configuration](configuration.md#client-registration-and-lifetime)) and an `Article` record generated
from an `article` content type with these elements: `title` (text), `body` (rich text), `post_date`
(date & time), `slug` (URL slug) and `rating` (custom). The inline-component example additionally
assumes a `callout` type whose multiple-choice `type` element allows several selections and offers a
`warning` option — that is why the generated `Callout.Type` is a collection.

## Read and write typed variants

Pass a record straight to the upsert:

```csharp
var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

var article = new Article
{
    Title = "On Roasts",
    PostDate = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero)
};

var result = await client.UpsertLanguageVariantAsync(identifier, article);
```

The generic get and upsert return `LanguageVariantModel<T>` — the typed `Elements` plus the same variant
metadata the untyped model carries:

```csharp
LanguageVariantModel<Article> variant =
    (await client.GetLanguageVariantAsync<Article>(identifier)).EnsureSuccess();

Article elements = variant.Elements;     // typed element values
Reference item = variant.Item;           // everything else is metadata
DateTime lastModified = variant.LastModified;
```

Listings whose variants all share one content type take the same type parameter —
`ListLanguageVariantsByItemAsync<T>`, `ListLanguageVariantsByTypeAsync<T>`,
`ListLanguageVariantsOfContentTypeWithComponentsAsync<T>`, and the `…PageAsync<T>` overloads of the last
two:

```csharp
var articles =
    (await client.ListLanguageVariantsByTypeAsync<Article>(Reference.ByCodename("article"))).EnsureSuccess();

foreach (var variant in articles)
{
    Console.WriteLine($"{variant.Language.Id}: {variant.Elements.Title}");
}
```

A listing by collection or space mixes content types, so it stays untyped. Once you know a variant's
type, project it with the same conversion the typed calls use:

```csharp
LanguageVariantModel<Article> article = client.ToTyped<Article>(variant);
```

## Environment binding and partial updates

> [!IMPORTANT]
> **A typed read matches elements by id**, so a record works only against the environment it was
> generated from. A response in which no element matches throws `InvalidOperationException` — that is a
> mismatch between your model and the environment, not an outcome of the call. Elements the record does
> not know are skipped, so a record generated before a type gained an element keeps working.
>
> **Writes key off codenames** and are portable across environments.

An upsert sends every element whose property value is **not null**, and omits the rest. Generated
Management records have nullable properties and no initializers, so a fresh record sends only the
elements you give non-null values:

```csharp
var article = new Article { Title = "On Roasts" };     // sends title only
await client.UpsertLanguageVariantAsync(identifier, article);
```

The SDK checks current values; it does not track assignment. Omitting an element leaves it unchanged.
That is not the same as clearing it.

## Element records and model values

Two families, one per authoring path. You do not mix them.

| | Element records | Model values |
|---|---|---|
| Used with | `LanguageVariantUpsertModel.Elements[]` | a generated record's properties |
| Carries its own `Element` reference | yes — it lives in an untyped array | no — the property identifies the element |
| Example | `CustomElement` | `CustomValue` |
| Generator needed | no | yes |

Most element kinds map to a bare value on a generated record:

| Element kind | Element record | Generated property |
|---|---|---|
| Text | `TextElement` | `string?` |
| Number | `NumberElement` | `decimal?` |
| Linked items, taxonomy, subpages | `LinkedItemsElement`, `TaxonomyElement`, `SubpagesElement` | `IEnumerable<Reference>?` |
| Asset | `AssetElement` | `IEnumerable<AssetReference>?` |
| Multiple choice | `MultipleChoiceElement` | generated enum — `TEnum?` for single-select, `IEnumerable<TEnum>?` for multi |
| Date & time | `DateTimeElement` | `DateTimeValue?` — instant plus `DisplayTimeZone` |
| URL slug | `UrlSlugElement` | `UrlSlugValue?` — slug plus `Mode` |
| Custom | `CustomElement` | `CustomValue?` — value plus `SearchableValue` |
| Rich text | `RichTextElement` | `RichTextValue?` — HTML plus `Components` |

The last four carry a companion field beside the value:

```csharp
using Kontent.Ai.Management.Models.Content;   // UrlSlugMode

var article = new Article
{
    PostDate = new DateTimeValue
    {
        Value = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero),
        DisplayTimeZone = "Europe/Prague"
    },
    Slug = new UrlSlugValue { Value = "on-roasts", Mode = UrlSlugMode.Custom },
    Rating = new CustomValue { Value = "{\"stars\":5}", SearchableValue = "5 stars" }
};
```

Each has an implicit conversion for the common case, so `PostDate = new DateTimeOffset(…)`,
`Slug = "on-roasts"` (custom mode) and `Rating = "{\"stars\":5}"` all work when the companion field is
not needed.

> [!IMPORTANT]
> A date & time is stored as a **UTC instant**; `DisplayTimeZone` only hints how an editor renders it and
> never changes the instant. The element takes a `DateTimeOffset` rather than a `DateTime` so the moment
> is unambiguous — a bare `DateTime` would be resolved against the machine's local zone. Whatever offset
> you supply is normalized to UTC on the wire.

For an element kind the SDK does not model, or to replay a variant fetched as raw JSON, `DynamicElement`
writes its `Value` to the wire as-is:

```csharp
new DynamicElement { Element = Reference.ByCodename("widget"), Value = "<opaque payload>" }
```

## Rich text and inline components

Rich text is authored as an HTML **string**. `RichTextBuilder` handles the error-prone part: keeping each
inline `<object data-id="…">` placeholder in sync with the matching entry in the `components` array.

Interpolate helper calls into the HTML. The builder mints the shared GUID, emits the placeholder and
records the component, so you never handle the GUID:

```csharp
using Kontent.Ai.Management.Models.Content;

var rt = new RichTextBuilder();

var content = rt.Build($"""
    <h1>On Roasts</h1>
    <p>See {rt.ItemLink(Reference.ByCodename("intro"), "the introduction")} first.</p>
    {rt.Component(new Callout { Type = [CalloutType.Warning] })}
    {rt.Asset(new AssetReference { Codename = "roasting_chart" })}
    """);

var article = new Article { Title = "On Roasts", Body = content };
await client.UpsertLanguageVariantAsync(identifier, article);
```

`Build` returns a `RichTextValue` — verbatim HTML plus the recorded components — ready to assign to a
rich-text property. `Callout` here is another generated record, embedded as an inline component.

| Helper | Emits | Use for |
|--------|-------|---------|
| `Component(IElementsModel item)` | `<object data-type="component" data-id="…">` **and** records the component | Embedding a generated record as an inline component |
| `LinkedItem(Reference)` | `<object data-type="item" data-…="…">` | Referencing an existing item inline |
| `ItemLink(Reference, linkText)` | `<a data-item-…="…">…</a>` | A hyperlink to an item |
| `Asset(AssetReference)` | `<figure data-asset-…="…">` | Embedding an asset |

`Component` accepts generated content-type records. Interpolation evaluates left to right, so call
order is record order. `Build` snapshots and resets the builder, so one instance can produce several
elements in turn; a builder nested inside a component's own rich-text body is independent of the outer
one.

> [!NOTE]
> The HTML passes through verbatim — the builder does not sanitize or validate markup, and is intended
> for trusted, code-authored content such as migration scripts. Helper attribute values and link text
> are HTML-encoded.

Rendering rich text for a website is the Delivery SDK's job, not this one.
