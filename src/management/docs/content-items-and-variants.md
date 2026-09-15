# Content items and language variants

Authoring content: creating items, setting their values per language, and moving them through a
workflow.

- [Items versus language variants](#items-versus-language-variants)
- [Create or upsert an item](#create-or-upsert-an-item)
- [Set variant values](#set-variant-values)
- [Create an item with its first variant](#create-an-item-with-its-first-variant)
- [Publish, schedule, and change workflow state](#publish-schedule-and-change-workflow-state)

Recipes below assume a configured `IManagementClient client` (see
[configuration](configuration.md#client-registration-and-lifetime)), a content type `article` with
`title` (text), `body` (rich text) and `post_date` (date & time) elements, and the languages `en-US` and
`de-DE`. No recipe depends on another having run; where one needs an identifier or a model, it declares
it.

## Items versus language variants

A **content item** is the language-agnostic wrapper: a name, a codename, a type, a collection. It holds
no element values.

A **language variant** holds the content for one language of that item. Element values live here, so
almost everything you think of as "editing content" is a variant operation.

```csharp
var item = await client.GetContentItemAsync(Reference.ByCodename("on_roasts"));
// item.Value.Name, .Codename, .Type - metadata only, no element values
```

## Create or upsert an item

```csharp
var created = await client.CreateContentItemAsync(new ContentItemCreateModel
{
    Name = "On Roasts",
    Codename = "on_roasts",
    Type = Reference.ByCodename("article"),
    Collection = Reference.ByDefaultCodename()   // optional
});
```

`UpsertContentItemAsync` is the re-runnable route: running it twice leaves one item, not two, so it is
what an import should use. **Identify the item by external ID.** If no item carries that external ID
the API creates one; if it does, the request updates it. Addressed by codename or internal ID an
upsert can only update — those identifiers cannot name an item that does not exist yet.

```csharp
var upserted = await client.UpsertContentItemAsync(
    Reference.ByExternalId("ext-item-456-brno"),
    new ContentItemUpsertModel
    {
        Name = "On Roasts",
        Type = Reference.ByCodename("article")
    });
```

To remove an item and all its variants:

```csharp
await client.DeleteContentItemAsync(Reference.ByCodename("on_roasts"));
```

## Set variant values

Set elements with one typed record per element kind. Each locates its target by `codename`, `id`, or
`external_id`, and carries a value shaped for that kind:

```csharp
using Kontent.Ai.Management.Models.LanguageVariants.Elements;

var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

var result = await client.UpsertLanguageVariantAsync(identifier, new LanguageVariantUpsertModel
{
    Elements =
    [
        new TextElement { Element = Reference.ByCodename("title"), Value = "On Roasts" },
        new DateTimeElement
        {
            Element = Reference.ByCodename("post_date"),
            Value = new DateTimeOffset(2018, 7, 4, 0, 0, 0, TimeSpan.Zero)
        }
    ]
});
```

**Omitted elements are left unchanged.** This is a partial update, not a replacement.

These records need no generator, so they work against any environment. With generated content-type
records you can pass a model instead, which is the better route for an application with a known schema —
see [models](models.md).

Reading back:

```csharp
var variant = await client.GetLanguageVariantAsync(identifier);
var all = await client.ListLanguageVariantsByItemAsync(Reference.ByCodename("on_roasts"));
```

Variants can also be enumerated across a collection, space, or content type —
`ListLanguageVariantsByCollectionAsync`, `…BySpaceAsync`, `…ByTypeAsync`. Those scale as
items × languages, so they have page overloads; see
[pagination](requests-and-results.md#pagination).

The full element-kind reference, including the companion fields some kinds carry, is in
[models](models.md#element-records-and-model-values).

## Create an item with its first variant

`CreateContentItemWithVariantAsync` does both in one call. A `<T>` overload takes a generated model
instead of element records.

```csharp
using Kontent.Ai.Management.Extensions;

var result = await client.CreateContentItemWithVariantAsync(
    new ContentItemCreateModel { Name = "On Roasts", Type = Reference.ByCodename("article") },
    Reference.ByCodename("en-US"),
    new LanguageVariantUpsertModel
    {
        Elements = [new TextElement { Element = Reference.ByCodename("title"), Value = "On Roasts" }]
    });
```

> [!IMPORTANT]
> **This is two calls, and it is not a transaction.** If the item is created but the variant upsert
> fails, the item stays — there is no rollback — and the returned failure carries the variant call's
> detail, so a partial failure **can leave** an item with no variant. A timeout or a lost response can
> also follow a write that was applied, so after an ambiguous failure reconcile the server state rather
> than assuming either outcome.
>
> To recover, do **not** re-run the composite: it attempts another create instead of resuming the
> previous operation, and a create that repeats an external ID is rejected rather than merged.
> Reconcile the item you already have and retry only the variant step — or drive the two calls
> yourself with [`UpsertContentItemAsync`](#create-or-upsert-an-item) by external ID, which creates
> the item the first time and updates it on every retry.

## Publish, schedule, and change workflow state

Each of these is a separate operation on an existing variant. Check the result of each before doing the
next.

| Operation | Method |
|---|---|
| Publish now | `PublishLanguageVariantAsync(identifier)` |
| Schedule publishing | `SchedulePublishingOfLanguageVariantAsync(identifier, schedule)` |
| Schedule both ends of a window | `SchedulePublishingAndUnpublishingOfLanguageVariantAsync(…)` |
| Unpublish | `UnpublishLanguageVariantAsync(identifier)` |
| Create a new draft of a published variant | `CreateNewVersionOfLanguageVariantAsync(identifier)` |
| Move to another workflow step | `ChangeLanguageVariantWorkflowAsync(identifier, change)` |

```csharp
var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");

(await client.PublishLanguageVariantAsync(identifier)).EnsureSuccess();
```

Scheduling takes an instant plus the zone the UI should display it in. The instant is what the API
acts on; `DisplayTimeZone` only affects how the date is shown to editors:

```csharp
await client.SchedulePublishingOfLanguageVariantAsync(identifier, new ScheduleModel
{
    ScheduledTo = new DateTimeOffset(2038, 1, 19, 4, 14, 8, TimeSpan.Zero),
    DisplayTimeZone = "Europe/London"
});
```

Moving between steps names both the workflow and the step, because an environment can have several
workflows:

```csharp
await client.ChangeLanguageVariantWorkflowAsync(identifier, new ChangeLanguageVariantWorkflowModel(
    workflow: Reference.ByDefaultCodename(),
    step: Reference.ByCodename("review")));
```

Defining the workflows themselves is [administration](administration.md#languages-and-workflow-definitions).

### Editing a published variant

A published variant cannot be edited in place. The API rejects the upsert with
`PublishedOrScheduledVariantCannotBeUpdated`; create a new version first, and check that it succeeded
before retrying the edit:

```csharp
var identifier = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");
var edit = new LanguageVariantUpsertModel
{
    Elements = [new TextElement { Element = Reference.ByCodename("title"), Value = "On Roasts, revised" }]
};

var result = await client.UpsertLanguageVariantAsync(identifier, edit);

if (!result.IsSuccess && result.Error?.ErrorCode == ManagementErrorCodes.PublishedOrScheduledVariantCannotBeUpdated)
{
    (await client.CreateNewVersionOfLanguageVariantAsync(identifier)).EnsureSuccess();
    result = await client.UpsertLanguageVariantAsync(identifier, edit);
}

result.EnsureSuccess();
```

Error codes are reused across unrelated conditions, so inspect `Message` too when the distinction
matters — see [results and failures](requests-and-results.md#branching-on-an-error-code).
