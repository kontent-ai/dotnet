# Content model

Changing the CMS schema: content types, snippets and taxonomy groups.

Examples assume a configured `IManagementClient client` — see
[configuration](configuration.md#client-registration-and-lifetime).

- [Create content types and snippets](#create-content-types-and-snippets)
- [Create taxonomies](#create-taxonomies)
- [Patch definitions](#patch-definitions)
- [Raw operations](#raw-operations)

This is the **CMS schema** — the types editors author against. The CLR records that mirror those types
are [models](models.md).

## Create content types and snippets

A type's elements are described by `ElementMetadataBase` subtypes, one per element kind —
`TextElementMetadataModel`, `RichTextElementMetadataModel`, `NumberElementMetadataModel`,
`AssetElementMetadataModel`, and so on.

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
            AllowedBlocks = [RichTextBlockType.Text, RichTextBlockType.Images]
        }
    ]
});
```

Snippets work the same way through `CreateContentTypeSnippetAsync`; a `ContentTypeSnippetCreateModel`
carries `Name`, `Codename` and `Elements`.

List them with `ListContentTypesAsync` and `ListContentTypeSnippetsAsync`.

## Create taxonomies

Terms nest recursively:

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

Listed with `ListTaxonomyGroupsAsync`.

## Patch definitions

Existing definitions are changed with a list of **patch operations**, not a full replace.

Content types and snippets address their target through a JSON-Pointer `path`. Use the
`ContentTypePatch` and `ContentTypeSnippetPatch` factories rather than writing that grammar: each method
bundles the path, a correctly-typed value and the verb, and returns a `ContentModelOperationBaseModel`,
so operations compose into one list.

```csharp
using Kontent.Ai.Management.Models.Types.Patch;
using Kontent.Ai.Management.Models.Types.Elements;

await client.ModifyContentTypeAsync(Reference.ByCodename("article"),
[
    ContentTypePatch.AddElement(new TextElementMetadataModel { Name = "Subtitle", Codename = "subtitle" }),
    ContentTypePatch.ReplaceGuidelines(Reference.ByCodename("body"), "Keep it under 300 words."),
    ContentTypePatch.MoveElementAfter(Reference.ByCodename("subtitle"), Reference.ByCodename("title")),
]);
```

The grammar is finite but irregular, and the method names tell you which rule applies: some collections
are set **as a whole array**, others are toggled **one entry at a time**.

| Operation | Shape |
|---|---|
| `ReplaceAllowedContentTypes(element, [...])` | whole set at once |
| `ReplaceAllowedItemLinkTypes(element, [...])` | whole set at once |
| `AddAllowedBlock` / `RemoveAllowedBlock(element, block)` | one rich-text block at a time |
| `ReplaceIsRequired`, `ReplaceGuidelines`, `ReplaceName`, … | a single scalar property |
| `AddElement`, `RemoveElement`, `MoveElementBefore` / `MoveElementAfter` | an element |
| `ReplaceContentGroup(element, group)` | reassign to a content group |

`ContentTypeSnippetPatch` mirrors this for `ModifyContentTypeSnippetAsync`, minus the content-group
operations — snippets have none.

Taxonomy groups address their target by a typed property-name enum instead of a path:

```csharp
await client.ModifyTaxonomyGroupAsync(Reference.ByCodename("categories"),
[
    TaxonomyGroupPatch.ReplaceName(Reference.ByCodename("coffee"), "Coffee beans"),
]);
```

The taxonomy `addInto` / `remove` / `move` term operations already carry typed values, so they are
constructed directly as their operation models.

Languages, spaces and custom apps have the same style of factory —`LanguagePatch`, `SpacePatch`,
`CustomAppPatch` — and belong with [administration](administration.md).

## Raw operations

Two escape hatches cover anything the factories do not model.

**Raw-path factories** take the `path` string directly, keeping the same fluent style:

```csharp
ContentTypePatch.ReplaceRaw(
    "/elements/codename:summary/maximum_text_length",
    new MaximumTextLengthModel { Value = 280, AppliesTo = TextLengthLimitType.Characters })
```

`AddIntoRaw`, `ReplaceRaw`, `RemoveRaw`, `MoveRawBefore` and `MoveRawAfter` are the full set.

**The operation records** give complete control — construct `ContentModelReplacePatchModel { Path = …,
Value = … }` and its add / move / remove siblings by hand.
