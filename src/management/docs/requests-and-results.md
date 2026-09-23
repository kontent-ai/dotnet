# Requests and results

How a Management call reports success and failure, how you address an entity, and how listings page.

Examples assume a configured `IManagementClient client` — see
[configuration](configuration.md#client-registration-and-lifetime).

- [Results and failures](#results-and-failures)
- [Exceptions](#exceptions)
- [Result helpers](#result-helpers)
- [Identifiers](#identifiers)
- [Pagination](#pagination)
- [Retrying an interrupted page](#retrying-an-interrupted-page)

## Results and failures

**API and transport failures are returned as results; cancellation and usage errors throw.**

Every `IManagementClient` method returns `IManagementResult` (void operations) or
`IManagementResult<T>`. A `4xx`/`5xx` from the API, a request that never reached the server, and a
response whose body could not be read all come back as `IsSuccess == false`.

> [!IMPORTANT]
> A `try`/`catch` around a call will not catch a failed request. `IsSuccess` is the check.

```csharp
var result = await client.CreateContentItemAsync(new ContentItemCreateModel
{
    Name = "On Roasts",
    Codename = "on_roasts",
    Type = Reference.ByCodename("article")
});

if (!result.IsSuccess)
{
    Console.WriteLine($"({result.StatusCode}) {result.Error?.Message}");
    Console.WriteLine($"Request ID: {result.Error?.RequestId}");   // quote this when reporting an issue

    foreach (var validationError in result.Error?.ValidationErrors ?? [])
    {
        Console.WriteLine(validationError.Message);
    }

    return;
}

ContentItemModel item = result.Value;
```

A result carries `IsSuccess`, `Value` (on success, `IManagementResult<T>` only), `Error` (on failure),
and the `StatusCode` / `RequestUrl` diagnostics.

`IError` exposes `Message`, `RequestId`, `ErrorCode`, `ValidationErrors`, and the underlying `Exception`
where one caused the failure — a response that could not be parsed as a Management error envelope, for
instance.

### Branching on an error code

Compare against the `ManagementErrorCodes` catalog rather than a magic number:

```csharp
if (!result.IsSuccess && result.Error?.ErrorCode == ManagementErrorCodes.PublishedOrScheduledVariantCannotBeUpdated)
{
    // Recovery for this one is a workflow step - see the variants guide.
}
```

`ManagementErrorCodes` is a curated set of the codes callers commonly act on: variant workflow-state
conflicts, duplicate external IDs, concurrency, and rate limits.

> [!WARNING]
> The codes are **not unique** — the API reuses some across unrelated conditions. Inspect `Message` as
> well when the distinction matters.

Recovering from a published variant needs a new version created first, and each step's result checked;
that recipe lives with [variant
lifecycle](content-items-and-variants.md#publish-schedule-and-change-workflow-state).

## Exceptions

A failed call is a result. These still throw:

| Cause | Exception |
|---|---|
| Cancellation via your `CancellationToken` | `OperationCanceledException` |
| A `null` argument | `ArgumentNullException` |
| An identifier kind the endpoint does not accept | `InvalidOperationException`, before any request is sent |
| A scope whose identifier is not configured | `InvalidOperationException`, naming the missing option |
| Invalid options | `OptionsValidationException`, when the client is built or registered |
| `EnsureSuccess()` on a failed result | `ManagementException`, carrying the `IError` |
| A typed variant response in which no element matches the record | `InvalidOperationException` — a mismatch between your model and the environment, not an outcome of the call. See [environment binding](models.md#environment-binding-and-partial-updates) |

An **expired timeout is not cancellation**: the request was sent and may have been applied, so it comes
back as a failed result.

The SDK never throws `ManagementException` on its own — it appears only where you opt in.

## Result helpers

| Helper | Use it for |
|---|---|
| `EnsureSuccess()` | Scripts and recipes where a failure should stop everything. Returns the value, throws `ManagementException` otherwise. |
| `TryGetValue(out var value)` | Branching without touching `IsSuccess` and `Value` separately. |
| `AsFailure<T>()` | Re-projecting a failed result onto another return type in your own multi-step helper, preserving error, status code and request URL. |

```csharp
var item = (await client.GetContentItemAsync(Reference.ByCodename("on_roasts"))).EnsureSuccess();
```

`AsFailure<T>()` is what stops a multi-step helper continuing after a failed step:

```csharp
var upload = await client.UploadFileAsync(file);
if (!upload.IsSuccess)
{
    return upload.AsFailure<AssetModel>();   // propagate the first failure, do not continue
}
```

For uploading and creating an asset specifically, the SDK already ships that helper — see
[assets](assets.md#upload-and-create-an-asset).

## Identifiers

Most operations target an entity through a `Reference`:

```csharp
var byCodename   = Reference.ByCodename("on_roasts");
var byId         = Reference.ById(Guid.Parse("9539c671-d578-4fd3-aa5c-b2d8e486c9b8"));
var byExternalId = Reference.ByExternalId("ext-item-456-brno");
```

| Entity | Identifier |
|---|---|
| Most entities | `Reference` — by codename, id, or external id |
| A language variant | `LanguageVariantIdentifier` — the item and language pair |
| Users | `UserIdentifier.ByEmail(…)` or `UserIdentifier.ById("usr_…")` |

A variant identifier has factories for the common case and a constructor for mixing kinds:

```csharp
var variant = LanguageVariantIdentifier.ByCodenames("on_roasts", "en-US");
var mixed   = new LanguageVariantIdentifier(Reference.ById(itemId), Reference.ByCodename("en-US"));
```

Objects you already fetched convert straight back: `ToReference()` on the models you list and act on
(`ContentItemModel`, `AssetModel`, `ContentTypeModel`, `LanguageModel`, …), and `ToIdentifier()` on a
fetched variant or an items-with-variants filter result.

```csharp
await client.DeleteContentItemAsync(item.ToReference());
await client.PublishLanguageVariantAsync(found.ToIdentifier());
```

Both reference by **id**, which is environment-specific. A script meant to run against more than one
environment should build the reference explicitly from a codename.

> [!NOTE]
> Not every endpoint accepts every identifier kind — some are ID-only, some forbid external IDs. Passing
> an unsupported kind throws `InvalidOperationException` before any request is sent.

## Pagination

| You want | Call | Behaviour |
|---|---|---|
| The whole set | `ListXAsync()` | Walks every continuation-token page, merges them, returns `IReadOnlyList<T>`. **All-or-nothing** — the first page failure short-circuits, so you never get a silently truncated set. Buffers everything. |
| One page at a time | `ListXPageAsync(token)` | One HTTP request, one ordinary result. Returns `ListingPage<T>` with `Items` and the next `ContinuationToken`; `null` means that was the last. |

For configuration data — types, languages, taxonomies — full materialization is a non-issue. Reach for
the page overload when a listing is genuinely large: content items, assets, the items-with-variants
filter and bulk-get, the language-variant listings by type, collection and space (which scale as
items × languages), and an async validation task's issues. The filter and bulk-get pair keep their
operation names — `FilterItemsWithVariantsAsync` / `FilterItemsWithVariantsPageAsync` and
`BulkGetItemsWithVariantsAsync` / `BulkGetItemsWithVariantsPageAsync` — but behave the same way.

```csharp
var result = await client.ListContentItemsAsync();
if (!result.IsSuccess)
{
    Console.WriteLine($"Failed to list content items: {result.Error?.Message}");
    return;
}

foreach (var item in result.Value)
{
    Console.WriteLine(item.Name);
}
```

A page walk advances the token only after the page has been processed:

```csharp
string? continuationToken = null;

do
{
    var result = await client.ListContentItemsPageAsync(continuationToken);
    if (!result.IsSuccess)
    {
        Console.WriteLine($"A page failed: {result.Error?.Message}");
        break;                                  // do not advance past a page you did not get
    }

    foreach (var item in result.Value.Items)
    {
        Console.WriteLine(item.Name);
    }

    continuationToken = result.Value.ContinuationToken;
}
while (continuationToken is not null);
```

## Retrying an interrupted page

Because the token is yours, an interrupted walk can be resumed rather than restarted. That matters most
under the [rate limit](https://kontent.ai/learn/docs/apis/management-api-v2/api-limitations): a
page-per-request walk is exactly the workload that reaches the per-minute one. The pipeline retries a
`429` with backoff, but a sustained limit outlives that and surfaces as a failed result.

Holding the last successful page's token makes recovery one request, instead of re-requesting every page
you already had:

```csharp
var page = (await client.ListContentItemsPageAsync(lastGoodToken)).EnsureSuccess();

foreach (var item in page.Items)
{
    Console.WriteLine(item.Name);
}

lastGoodToken = page.ContinuationToken;
```

> [!NOTE]
> The token is opaque and server-issued; how long it stays valid is the API's contract, not the SDK's.
> It is dependable across a backoff. Verify before relying on one across a long pause or a process
> restart.
