# Kontent.Ai.Management

All notable changes to this package are documented here.
Entries before the move to this monorepo were imported from the GitHub Releases of
[kontent-ai/management-sdk-net](https://github.com/kontent-ai/management-sdk-net).

## Unreleased

### Breaking changes

- **The `Enumerate…PagesAsync` page streams are replaced by `List…PageAsync` single-page calls.** The streams returned `IAsyncEnumerable<IManagementResult<IReadOnlyList<T>>>` — three wrappers to unpack before reaching an item, which meant they delivered none of what an async stream is normally reached for: you could not `await foreach` over the items, and LINQ operated on page results rather than content. The failure signal also arrived per element, so a loop body that skipped `IsSuccess` hit the failed page's `null` value instead of the error.

  Each stream is replaced by a call that fetches exactly one page and hands back the continuation token:

  ```csharp
  string? continuationToken = null;
  do
  {
      var page = (await client.ListContentItemsPageAsync(continuationToken)).EnsureSuccess();
      Process(page.Items);
      continuationToken = page.ContinuationToken;
  }
  while (continuationToken is not null);
  ```

  One call is one result, exactly like every other method on the client. Surfacing the token also makes an interrupted walk **recoverable**, which the stream was not. Resilience retries a `429` three times with backoff, but a sustained rate limit — the failure a bulk walk over a large environment actually provokes — outlives that and surfaces as a failure. The stream ended there and gave back no token, so the only way on was to re-enumerate from the first page, re-issuing every request that had already succeeded against the very limit that stopped it. Holding the last successful page's token turns that into one request.

  | Removed | Replacement |
  |---|---|
  | `EnumerateAssetPagesAsync` | `ListAssetsPageAsync` |
  | `EnumerateContentItemPagesAsync` | `ListContentItemsPageAsync` |
  | `EnumerateItemsWithVariantsByFilterPagesAsync` | `ListItemsWithVariantsByFilterPageAsync` |
  | `EnumerateItemsWithVariantsByBulkGetPagesAsync` | `ListItemsWithVariantsByBulkGetPageAsync` |
  | `EnumerateLanguageVariantsByTypePagesAsync` | `ListLanguageVariantsByTypePageAsync` |
  | `EnumerateLanguageVariantsOfContentTypeWithComponentsPagesAsync` | `ListLanguageVariantsOfContentTypeWithComponentsPageAsync` |
  | `EnumerateLanguageVariantsByCollectionPagesAsync` | `ListLanguageVariantsByCollectionPageAsync` |
  | `EnumerateLanguageVariantsBySpacePagesAsync` | `ListLanguageVariantsBySpacePageAsync` |

  The materialized `List…Async` methods are unchanged, and remain the right default.

### Added

- **A page call for the async validation task issues.** `ListAsyncValidationTaskIssuesPageAsync` joins the materialized `ListAsyncValidationTaskIssuesAsync`. This is the listing that scales hardest — a task over a broken environment reports issues proportional to items × variants × elements — and it was the one unbounded listing with no paged access at all, while narrower ones (the variant listings, assets, items) already had it.
- **`ListingPage<T>`**, the page a `List…PageAsync` call returns: the page's `Items`, and the `ContinuationToken` that fetches the next one (`null` on the last page). It is a sealed class rather than a record: its only reference-typed member is a list, so synthesised equality would have compared that by reference and quietly reported two pages holding identical items as unequal. It stays immutable; it just does not claim value semantics it cannot deliver.

### Fixed

- **The paging helper no longer reads a continuation token off a disposed response.** Mapping a page to a result disposes the underlying Refit response; the walker then read the next token from it. The value survived because Refit buffers the body, but the ordering was load-bearing and invisible at the call site. The token is now read before the response is mapped. No behavior change.

- **A response body that is not the error envelope no longer risks an unhandled exception.** Parsing failures were caught as `JsonException` alone, so anything else raised while reading the body — an encoding failure on a malformed payload, say — escaped the result pattern and was thrown at the caller. The catch now covers any non-fatal exception, matching the Delivery and Sync SDKs; the resulting error still carries the raw body and the original request failure.

## 9.0.0-rc.2 (2026-08-12)  _(prerelease)_

### Breaking changes

- **`Reference` moved from the asset-folder and taxonomy-group PATCH bases onto the operations that need it, where it is `required`.** Both bases declared a nullable `Reference`, so a `remove`, `rename`, `move` or `replace` operation could be constructed without the reference the API demands — the compiler was fine with it and the request failed at the server. Each operation now declares its own: `required` on the ones that target something (`AssetFolderRemovePatchModel`, `AssetFolderRenamePatchModel`, `TaxonomyGroupRemovePatchModel`, `TaxonomyGroupMovePatchModel`, `TaxonomyGroupReplacePatchModel`), and still optional on `addInto`, where it names the parent to add into and its absence means the root. This matches the collection patch models, which were already shaped this way. The wire format is unchanged; code that already set `Reference` on these operations still compiles, and code that did not now fails to compile instead of failing at the API.

### Changed

- **`EnvironmentId` is no longer required when you only call subscription endpoints.** Subscription-scoped endpoints resolve against `/v2/subscriptions/{id}` and never touch an environment, but validation demanded an `EnvironmentId` regardless — so a subscription admin listing projects had to invent an environment GUID the SDK would never use. Each scope's client is now built only when its identifier is configured, and `EnvironmentId` is validated for format only when supplied, exactly as `SubscriptionId` already was.

  Calling into a scope you did not configure fails immediately, naming the option — before the request is built, so you never see the API's `404` for a path with an empty segment:

  ```
  EnvironmentId is not configured. Set ManagementOptions.EnvironmentId to call environment endpoints.
  ```

  Configuring **neither** identifier is still rejected at registration: that client could call nothing at all. Every existing configuration behaves exactly as before — this only accepts input that was previously refused.

### Fixed

- **The doc samples assert success rather than that a result object exists.** Forty of them ended in `Assert.NotNull(response)` against an `IManagementResult`, which is never null — so a failed call passed. Replacing that with `EnsureSuccess()` immediately surfaced five sample fixtures that had drifted out of step with their models and could no longer deserialize; those are refreshed from the fixtures the domain tests use.

- **The pass-through `CreateRefitSettings` wrapper is gone**, and the deliberate `ScheduleResponseModel` date divergence is now recorded so it is not "corrected" later.

- **IntelliSense wording corrections.** The single-item custom-app operations described themselves in the plural, `UpdatePreviewConfigurationAsync` was documented as a "Modify" (this SDK's word for `PATCH`, which it is not) with a parameter described as project-scoped, and a subscription-user method read "Retrieve a user metadata". Two enum members had typos in their summaries.

- **The unused `Microsoft.Extensions.Logging.Abstractions` reference is gone**, so it no longer lands in the published package as a dependency nobody needs.

- **The doc samples for importing content check their results.** Every one of the nineteen discarded the `IManagementResult` it received, so a failed call passed the test and the published sample taught ignoring the result pattern the SDK is built around. They now use `EnsureSuccess()`, which is both a real assertion and the idiomatic sample code — and each sample is backed by a response fixture that actually deserializes, so the assertion has something to check rather than passing on an empty body.

- **The client factory no longer relabels an exception that came from your own registration.** `Get(name)` caught `InvalidOperationException` and reported it as a missing client — but the registration runs during resolution, so a `configureHttpClient` that rejected its input came back as "No management client registered with name '…'", pointing at the wrong thing entirely. A genuinely missing registration still says so.

- **A doc sample no longer reads a bare timestamp in the machine's time zone.** Three samples fed `DateTime.Parse` into a `DateTimeOffset` scheduling parameter, which is exactly the ambiguity the SDK's date convention exists to prevent — taught in code people copy. They now construct the offset explicitly, as the README sample already did.

- **The README no longer offers a Refit-settings hook that was removed.** `ManagementClientBuilder` customizes the resilience pipeline; the Refit hook it also advertised is gone.

- **The `X-KC-SOURCE` header keeps naming the integration that made the call.** Attribution matched the SDK assembly by full name, which carries the version — and nothing pins `AssemblyVersion`, so the reference an integration recorded when it was built stopped matching on the first SDK release after that. The header then went silently missing for every consumer who had not rebuilt. Matching is now by simple name.

- **The interface says what happens when you call into a scope you did not configure.** Since `EnvironmentId` became optional for subscription-only clients, every environment operation throws `InvalidOperationException` when it is missing — the same guard the subscription operations already documented, but stated nowhere for the ~80 methods on the other side. `IManagementClient`'s own remarks now describe both scopes and the guard once, rather than repeating an `<exception>` tag on every method.

- **The documented error-handling model matches what the SDK does.** The README, the upgrade guide and the `IManagementResult` / typed-variant IntelliSense all said network-level and serialization failures "still propagate as exceptions". They do not, and have not since the result pattern landed: a transport failure that never reached the server and a response whose body could not be read are both failed results, carrying the exception in `Error.Exception`. A consumer following the old text wrote a `catch` that never fires and skipped the `IsSuccess` check that would have caught the failure. The docs now state what actually throws — cancellation, argument and configuration validation, `EnsureSuccess()`, and a typed-variant projection onto a record that no longer matches the content type — and the behaviour is pinned by tests.

- **The README now says how to configure a subscription-scoped call.** It listed `SubscriptionId` in the options table and mentioned "an API key with subscription scope", but never said the Subscription API key is a different credential from the Management API key or where to get one. There is now a worked example and a pointer to `https://app.kontent.ai/subscription/<subscription-id>/api-keys`, which only a subscription admin can use.

## 9.0.0-rc.1 (2026-08-07)  _(prerelease)_

Targets .NET 10, completing the framework move that the `9.x` line was always heading for, and upgrades Refit across four major versions. The result pattern, transport architecture and model conventions introduced in the earlier betas are unchanged — see the [9.0.0-beta-1 release notes](release-notes-9.0.0-beta-1.md) for that overview.

> [!WARNING]
> Still a **prerelease**. Install with `--prerelease` — without that flag you get the stable `8.x` API, which these notes do **not** describe.

### Breaking changes

- **`net8.0` → `net10.0`.** There is no multi-targeting, so a project on .NET 8 cannot install this release — restore fails with `NU1202`. Move to .NET 10 first.
- **`new FileContentSource(stream, …)` now rejects a stream that cannot seek**, with an `ArgumentException` naming the parameter. The upload endpoint needs the size up front: without a `Content-Length` the request goes out chunked and is refused with *"the file is bigger than the maximal allowed limit (2 GB)"* — regardless of the actual size, and verified against the live API. A non-seekable stream has no length to declare, so this overload could never produce a successful upload; the error simply arrived from the server, describing the wrong problem. It is now refused where the stream is passed in.

  Nothing that worked stops working — there was no combination in which a non-seekable stream uploaded successfully. If you were passing one, buffer it first or use the `byte[]`/file-path overload:

  ```csharp
  // Before — always failed, with an error about a 2 GB limit
  var source = new FileContentSource(httpResponseStream, "photo.jpg", "image/jpeg");

  // After — buffer, so the length is known
  using var buffered = new MemoryStream();
  await httpResponseStream.CopyToAsync(buffered);
  var source = new FileContentSource(buffered.ToArray(), "photo.jpg", "image/jpeg");
  ```
- **The client interfaces no longer carry `IDisposable` / `IAsyncDisposable`; the concrete clients do.** Disposal exists for one situation - a client built outside a container, which owns its own transport and must release it. Putting it on the interface meant every consumer holding `IManagementClient` was offered a `Dispose()` that, on the container path, released nothing and must not be called: the container owns that lifetime. `ManagementClientBuilder.Build()` now returns the concrete `ManagementClient`, which is `IDisposable` and `IAsyncDisposable`, so disposal stays available exactly where it means something.

  `await using var client = ManagementClientBuilder…Build();` is unchanged, and so is every DI usage. The only code that breaks widened the builder result to the interface and then disposed it:

  ```csharp
  // Before - no longer compiles, because the interface has no Dispose
  IManagementClient client = ManagementClientBuilder…Build();
  client.Dispose();

  // After - keep the concrete type, or just use var
  var client = ManagementClientBuilder…Build();
  client.Dispose();
  ```

  Container-resolved clients are still disposed by the container, which checks the runtime type rather than the registered service type - so nothing changes there.
- **The `configureRefit` parameter is gone from all five `AddManagementClient` overloads, and `ManagementClientBuilder.ConfigureRefit` is removed.** The hook handed out the transport library's settings object, but every value it could reach was load-bearing rather than configurable — the parameter-key formatter matches the API's casing, and the serializer options carry the converters, naming policy and nesting limit the wire format depends on. Overriding them broke requests silently. Delete the argument or the builder call; the SDK's own tests only ever used it to assert the callback fired.
- **Enum values are now read case-sensitively.** Only the exact wire token is accepted: `"modular_content"` binds, `"MODULAR_CONTENT"` and the C# member name `"LinkedItems"` now throw `JsonException` instead of being coerced. The Management API emits canonical tokens and `ContentModelSnapshot.FromJson` only ever consumes `ToJson` output, so this affects hand-written JSON. Writing is unchanged, and numeric tokens are still rejected in both directions.

### Changed

- **`AddManagementClient` gained the overloads its sibling SDKs already had**, so the three register a client the same way: a pre-built options instance, and options configured with access to the `IServiceProvider`. Nothing was removed, and existing calls are unaffected — this closes gaps rather than reshaping the surface.
- **Cancellation now throws; other transport failures are results.** Refit's upgrade changed the
  contract: exceptions raised in the HTTP pipeline are captured into the response rather than thrown.
  A network failure, DNS failure or resilience-pipeline rejection is therefore an unsuccessful result
  carrying the exception, consistent with how every other failure in this SDK is reported. Cancellation
  is the exception to that: when the caller's token fires, the `OperationCanceledException` is rethrown,
  so `Task.IsCanceled`, `Task.WhenAll` and cancellation handlers behave as they do everywhere else in
  .NET. Previously **all** of these threw. An expired `HttpClient.Timeout` is *not* cancellation, even
  though .NET surfaces it as a `TaskCanceledException`. This one matters on a write API: the request was
  sent and the server may have applied it, so it is reported as a failed result carrying the exception,
  never as the caller withdrawing a request that never happened.
- **Transport failures report status `0`.** `IManagementResult` now carries `(HttpStatusCode)0` for that case rather than an invented code. Responses that did arrive are unaffected.

- **`ManagementOptions.Timeout`** sets the ceiling on one call, covering every retry attempt and the waits between them. Defaults to 30 minutes.

### Fixed

- **An empty pre-release label no longer produces a trailing hyphen in `X-KC-SOURCE`.** A package identifying itself with `[assembly: SourceTrackingHeader("MyPackage", 2, 0, 0, "")]` was reported as `MyPackage;2.0.0-`, which is not a valid SemVer version. An empty label now counts as no label, matching what passing `null` already did.
- **`GetFullFolderPath` no longer starts a path with a separator.** An ancestor folder with an empty name still contributed a segment, so a folder below it came back as `\\Child` rather than `Child`. Unnamed ancestors are now skipped.
- **A long upload is no longer cut off at 100 seconds.** This SDK deliberately configures no per-attempt timeout, because an asset upload takes as long as the file is large and the link is slow. But `HttpClient`'s own 100-second default bounds the *whole* call — every attempt and all the backoff between them — and nothing raised it, so it capped exactly the uploads the missing per-attempt timeout was meant to protect. It also silently truncated retries: a `429` carrying `Retry-After: 60` spent most of the budget before the next attempt began. The ceiling is now `ManagementOptions.Timeout`, defaulting to 30 minutes — sized against the documented 2 GB asset limit, which is roughly what that carries over a 10 Mbps link.
- **Uploads always declare a `Content-Length`.** A source that could not report its size sent the request chunked, and the endpoint rejects that outright — reporting "the file is bigger than the maximal allowed limit (2 GB)" no matter how small the file actually was. Every source now carries a length, so the request the SDK builds is one the API can accept.
- **Long-running applications pick up DNS changes instead of pinning the address resolved at startup.** The registered client is a singleton and takes its `HttpClient` from `IHttpClientFactory` once, so the handler chain it holds was never rotated — the factory only hands a fresh chain to a *new* `CreateClient` call. Connections now recycle every two minutes, matching the factory's own default handler lifetime. This matters when the endpoint's address changes underneath a process that stays up for days: a failover, a scale event, or any re-pointing upstream. Configuring your own primary handler via `configureHttpClient` still overrides this, as before.

### Dependencies

Shipped floors on `Kontent.Ai.Management` moved up, all .NET 10 aligned:

- `Microsoft.Extensions.*` (`Configuration.Abstractions`, `Logging.Abstractions`, `Options.ConfigurationExtensions`, `Options.DataAnnotations`) **9.0.15** → **10.0.10**.
- `Microsoft.Extensions.Http.Resilience` **9.6.0** → **10.8.0**.
- `Refit` and `Refit.HttpClientFactory` **10.2.0** → **14.0.1**.

### Internal

No consumer-visible effect:

- Enum wire tokens now travel on `[JsonStringEnumMemberName]` and serialize through the built-in `System.Text.Json` converter. The custom converter existed only because that attribute did not exist on .NET 8. All 140 members across 36 enums keep their exact tokens, verified by round-trip — including the ones that are not snake_case (`light-purple`, `fullScreen`, `asc`, `modular_content`).
- Refit 14 builds request logic at compile time rather than by reflection; the Management interfaces generate completely and gained that with no changes.


## 9.0.0-beta-5 (2026-08-03)  _(prerelease)_

A packaging-only fix on top of 9.0.0-beta-4. No API or behavior change — if you are already restoring beta-4 successfully, there is nothing new here.

> [!WARNING]
> This is a **prerelease**. Install it with `--prerelease` — without that flag you get the stable `8.x` API, which these notes do **not** describe. Breaking changes may still land between prereleases until the first stable `9.x` ships; pin an exact version if you need stability during the beta. For production today, stay on the latest stable `8.x`.

### Fixes

- **Restore no longer fails with NU3012.** The package now depends on `Refit` / `Refit.HttpClientFactory` **10.2.0** instead of 10.1.6. The 10.1.6 packages are author-signed with a certificate that has since been revoked, so restoring them fails signature verification — on by default on Windows, and enabled via `DOTNET_NUGET_SIGNATURE_VERIFICATION=true` on Linux CI. 10.2.0 is the same code re-signed with a valid certificate.

  This was not avoidable downstream. Even when another dependency required 10.2.0 and version resolution settled there, NuGet still downloaded and verified 10.1.6 while walking the graph, and failed before resolution completed. Consumers who worked around it by pinning Refit directly can drop that pin.

### Other

- Built from the [kontent-ai/dotnet](https://github.com/kontent-ai/dotnet) monorepo. Package ID and API surface are unchanged.

## 9.0.0-beta-4 (2026-07-20)  _(prerelease)_

Fourth beta of the modernized Management SDK, and primarily a **security** release: it closes a path-traversal issue in how caller-supplied identifiers become request paths. It also fixes several correctness and reliability bugs and adds a small ergonomic improvement. For the full overview of the rewrite (result pattern, Refit + `System.Text.Json` transport, materialized listings, DI + fluent builder, immutable strongly-typed models), see the [9.0.0-beta-1 release notes](release-notes-9.0.0-beta-1.md).

> [!WARNING]
> This is a **prerelease**. Install it with `--prerelease` — without that flag you get the stable `8.x` API, which these notes do **not** describe. Breaking changes may still land between prereleases until the first stable `9.x` ships; pin an exact version if you need stability during the beta. For production today, stay on the latest stable `8.x`.

### Security

An untrusted string passed as an identifier could previously escape its path segment and retarget a request to a different Management API endpoint under the same API key — a path-traversal / confused-deputy issue (CWE-22). Identifiers are now validated at the URL boundary, so a value containing `/` or `\`, equal to `.` / `..`, or empty/whitespace-only is rejected with an `ArgumentException`. Normal identifiers are unaffected. This covers:

- **Codenames, external ids, user ids, and emails** — e.g. `Reference.ByCodename("../../webhooks-vnext")` or `UserIdentifier.ByEmail(...)`.
- **Upload file names** — the `fileName` given to `FileContentSource` (a `..` would have retargeted the upload to the environment root).
- **Empty identifiers** — an empty value used to collapse a single-resource call onto its parent collection route (e.g. `GetSubscriptionUserAsync(UserIdentifier.ById(""))` hit the *list users* endpoint); it now throws.

**One behavior change to note:** an **external id that legitimately contains a `/`** is now rejected rather than silently mis-routed. (The `8.x` SDK encoded it as `%2F`; the modernized transport can't do that without either double-encoding other values or losing the traversal protection.) Address such a resource by id or codename instead.

### Breaking changes

- **`ScheduleModel.ScheduleTo` → `ScheduledTo`.** The property now matches its wire field (`scheduled_to`) and the neighboring `SchedulePublishAndUnpublishModel.PublishScheduledTo` / `.UnpublishScheduledTo`. It's a `required` property, so the compiler flags every call site.

### Improvements

- **Configuration-based client registration now accepts the HTTP customization hooks.** The `AddManagementClient` overloads that bind from `IConfiguration` / `IConfigurationSection` take the same optional `configureHttpClient` / `configureResilience` / `configureRefit` parameters as the action-based overloads.

### Fixes

- **`DynamicElement` no longer drops explicit `null` values.** A fetched element with `"value": null` re-upserted through `DynamicElement` used to have its `value` silently omitted from the request, turning an explicit null into an absent property. It now round-trips.
- **Strongly-typed reads match element ids case-insensitively.** A model whose `[KontentElement]` id was not lowercase previously failed to bind that element with no error. First-time use of typed models from multiple threads is also no longer racy.
- **`Retry-After` response headers in HTTP-date form are honored**, not just the delta-seconds form.
- **Retried requests no longer send duplicate `X-KC-SDKID` / `X-KC-SOURCE` tracking headers.**
- **Serializing an out-of-range enum value throws** instead of sending an invalid token (e.g. `"99"`) to the API.
- **Null arguments to the client convenience extensions and asset-folder helpers throw `ArgumentNullException`** instead of a `NullReferenceException`.

### Installation

```bash
dotnet add package Kontent.Ai.Management --prerelease
```

### Requirements

- .NET 8.0
- A Kontent.ai environment with Management API v2 access (a Management API key)

## 8.3.1 (2026-07-17)

Security hotfix. Recommended for all 8.x users.

### Security
#### Path traversal via unescaped identifiers

Some resource identifiers (codenames, emails, and upload file names) were inserted into Management API request paths without escaping, so a caller-supplied value containing path-traversal characters could retarget a request to a different Management API endpoint under the same API key.

Identifier values are now consistently encoded and validated as a single URL path segment before the request is sent, so they can no longer affect which endpoint a request targets.

### Notes

- **Minor wire change:** identifier values are now percent-encoded consistently, so an email goes out as e.g. `test%40kontent.ai` instead of `test@kontent.ai`. The Management API decodes these identically — no action needed.
- Some malformed identifier values now throw `ArgumentException`; ordinary values continue to work unchanged.
- No public API surface changed and no members were removed.

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/8.3.0...8.3.1

## 9.0.0-beta-3 (2026-07-13)  _(prerelease)_

Third beta of the modernized Management SDK — an **ergonomics-and-alignment** release. It rounds off the API surface with small, explicit conveniences drawn from real call-site friction, finishes the naming and collection-type conventions the rewrite started, and modernizes every doc sample to teach the patterns the SDK actually ships. For the full overview of the rewrite (result pattern, Refit + `System.Text.Json` transport, materialized listings, DI + fluent builder, immutable strongly-typed models), see the [9.0.0-beta-1 release notes](release-notes-9.0.0-beta-1.md).

> [!WARNING]
> This is a **prerelease**. Install it with `--prerelease` — without that flag you get the stable `8.x` API, which these notes do **not** describe. Breaking changes may still land between prereleases until the first stable `9.x` ships; pin an exact version if you need stability during the beta. For production today, stay on the latest stable `8.x`.

> [!IMPORTANT]
> **Upgrading from `8.x`?** Read the [upgrade guide](upgrade-guide.md) first — it covers every breaking change with before/after examples. If you're already on `9.0.0-beta-2`, see [API refinements](#api-refinements-breaking-vs-900-beta-2) below for the beta-to-beta changes.

### New

- **`ToReference()` on fetched models.** The models you list and act on (`ContentItemModel`, `AssetModel`, `ContentTypeModel`, `LanguageModel`, `WorkflowModel`, and five more) convert straight back into the `Reference` other calls expect: `client.DeleteContentItemAsync(item.ToReference())`. References by **id** deliberately — the docs on each method point you to `Reference.ByCodename(...)` for cross-environment scripts. *(Closes the last open ask from [#272](https://github.com/kontent-ai/management-sdk-net/issues/272); the proposed implicit conversion was declined in favor of this explicit form.)*
- **`ToIdentifier()` for variant workflows.** A fetched variant or an items-with-variants filter result feeds directly into publish/upsert/workflow calls — `client.PublishLanguageVariantAsync(found.ToIdentifier())` — and `ToVariantIdentifier()` chains filter results into a bulk get. No more manual `(item, language)` reassembly in filter → act loops.
- **`GetPublishedLanguageVariantAsync<T>`.** The typed projection of the published-variant endpoint, with parity to `GetLanguageVariantAsync<T>`.
- **Patch factories for every property-name domain.** `LanguagePatch`, `SpacePatch`, `TaxonomyGroupPatch`, and `CustomAppPatch` join `ContentTypePatch`: each names the property and takes the correctly-typed value, replacing the `object Value` guessing game — `LanguagePatch.FallbackLanguage(Reference.ByCodename("en-US"))`, `SpacePatch.RootItem(null)` to unset, `CustomAppPatch.AddAllowedRole(role)`.
- **Page streams for the variant listings.** `EnumerateLanguageVariantsByTypePagesAsync`, `…ByCollectionPagesAsync`, `…BySpacePagesAsync`, and `…OfContentTypeWithComponentsPagesAsync` stream the most unbounded listings (they scale as items × languages) one page at a time, like the existing asset/item streams.
- **`AsFailure<T>()` is public.** The primitive the SDK's own composite helpers use to propagate the first failure onto a different result type is now available for your own multi-step helpers (upload → create → link) — it preserves the error, status code, and request URL, and throws if misused on a successful result.
- **Element default values in one expression.** Each default-value model gained a convenience constructor: `DefaultValue = new TextElementDefaultValueModel("Untitled")` instead of the three-level `{ Global = new() { Value = … } }` initializer (which still works).

### Improvements

- **Less required ceremony.** Workflow step role lists (`RoleIds`, `CreateNewVersionRoleIds`, `UnpublishRoleIds`) default to empty — "no role restriction" no longer needs four explicit empty lists (the wire payload is unchanged; empty arrays still always serialize). `CollectionReplacePatchModel.PropertyName` defaults to `Name`, its only valid value today.
- **`Retry-After` honored everywhere.** The default resilience pipeline now respects a server-provided `Retry-After` delay on any retried response (e.g. `503`), not just `429`.
- **Content-model snapshot is explicitly experimental.** `ExportContentModelAsync` / `ContentModelSnapshot` now carry `[Experimental("KAIM001")]` — the feature and its JSON format are not yet a supported contract; suppress the diagnostic to opt in.
- **Doc samples teach the intended patterns.** The repository's code samples now use `EnsureSuccess()` instead of unchecked `.Value` unwrapping, the identifier factories, the patch facades instead of raw path strings, the one-call asset upload extensions, and modern collection expressions throughout.

### API refinements (breaking vs `9.0.0-beta-2`)

Beta-to-beta breaking changes — the compiler surfaces each:

- **All model collection properties are `IReadOnlyList<T>`.** The remaining `IEnumerable<T>` properties (69 declarations) converged on the convention the rest of the SDK already used, and `RichTextElementMetadataModel`'s six `ISet<…>` restriction properties did the same — so collection expressions now work everywhere: `AllowedBlocks = [RichTextBlockType.Text, RichTextBlockType.Images]`. If you assigned a deferred LINQ query directly, materialize it (`[.. items.Select(…)]`) — which also removes a subtle re-enumeration risk when retries re-serialize a request body.
- **`UpdateUserRolesAsync` takes a dedicated request model.** The body is now `UserRolesUpdateModel { CollectionGroups = … }` — previously it took the response-shaped `UserModel`, forcing a fake required `user_id` into a body the API identifies via the URL.
- **`UpsertLanguageVariantAsync(identifier, LanguageVariantModel)` moved to the extensions tier** (`Kontent.Ai.Management.Extensions`), joining its `UpsertContentItemAsync(Reference, ContentItemModel)` twin: the interface carries one method per API operation; fetched-model adapters are extensions. Call sites keep the same syntax — add the `using` if you don't have it.
- **Naming aligned, wire format untouched:**
  - Identifier-pair properties unified to the bare style: `WorkflowReference` / `WorkflowStepReference(s)` → `Workflow` / `Step(s)` (webhook workflow-transition triggers, variant filter), `TaxonomyReference` / `TermReferences` → `TaxonomyGroup` / `Terms`, `SnippetIdentifier` → `Snippet` (snippet element, URL-slug dependency).
  - `WorkflowStepColorModel` → `WorkflowStepColor` (an enum, not a model) and `RoleModel` → `UserRoleModel` (matching `EnvironmentRoleModel` / `SubscriptionUserRoleModel`).
  - Default-value models unified to `{Kind}ElementDefaultValueModel`, including `DateElementDefaultValueModel` → `DateTimeElementDefaultValueModel` to match `DateTimeElementMetadataModel`.
- **Asset-folder ids are `Guid`.** `AssetFolder.Id`, `AssetFolderHierarchy.Id`, and `AssetFolderLinkingHierarchy.Id` were `string`s holding GUIDs; they're typed `Guid` now (the zero GUID keeps meaning "outside any folder"), and the `AssetExtensions` folder-lookup helpers take `Guid` accordingly.

### Installation

```bash
dotnet add package Kontent.Ai.Management --prerelease
```

### Requirements

- .NET 8.0
- A Kontent.ai environment with Management API v2 access (a Management API key)

## 9.0.0-beta-2 (2026-07-09)  _(prerelease)_

Second beta of the modernized Management SDK — a **fixes-and-refinements** release. No new surface area; it hardens the beta-1 rewrite with bug fixes and small API corrections drawn from beta feedback and an independent code review. For the full overview of the rewrite (result pattern, Refit + `System.Text.Json` transport, materialized listings, DI + fluent builder, immutable strongly-typed models), see the [9.0.0-beta-1 release notes](release-notes-9.0.0-beta-1.md).

> [!WARNING]
> This is a **prerelease**. Install it with `--prerelease` — without that flag you get the stable `8.x` API, which these notes do **not** describe. Breaking changes may still land between prereleases until the first stable `9.x` ships; pin an exact version if you need stability during the beta. For production today, stay on the latest stable `8.x`.

> [!IMPORTANT]
> **Upgrading from `8.x`?** Read the [upgrade guide](upgrade-guide.md) first — it covers every breaking change with before/after examples. If you're already on `9.0.0-beta-1`, see [API refinements](#api-refinements-breaking-vs-900-beta-1) below for the beta-to-beta changes.

### Fixes

- **Double-encoded URL path segments.** A codename or external id containing reserved characters (space, `@`, `&`, …) was percent-encoded twice, so the request went out as e.g. `external-id/with%2520space` and the server never matched it. It's now encoded exactly once on the wire. Affected every by-codename / by-external-id call. *(Caveat: a literal `/` in an external id still can't round-trip through the API's catch-all path — treat that as unsupported.)*
- **Asset upload could race the send / break retries.** `UploadFileAsync` disposed the upload body the moment the task was handed back, racing the request and defeating the retry-driven re-reads it exists for. It now holds the content for the full request lifetime.
- **`url_slug` / custom elements wrote `"value": null`.** A server-derived (autogenerated) slug or an unset custom value now **omits** `value` rather than sending `null`, matching the envelope's null-means-omit contract used everywhere else.
- **Multiple-choice options silently dropped on read.** An option missing from your generated enum used to be silently deselected on a fetch → modify → upsert (silent data loss). The SDK now throws a clear "regenerate your model" error instead — matching the strictness the write side already had.
- **Subscription endpoints without a `SubscriptionId`.** Calling a subscription-scoped method without configuring `SubscriptionId` produced a malformed URL and a confusing `404`. It now throws a clear `InvalidOperationException`, and the subscription client is built only when a `SubscriptionId` is present.
- **Retries no longer risk duplicating writes.** The default resilience pipeline is now idempotency-aware: `429 Too Many Requests` is retried for every method (the request was rejected, not applied), but transient network failures and `5xx` are retried **only for idempotent methods** (`GET` / `HEAD` / `OPTIONS` / `PUT` / `DELETE`). A `POST` that times out *after* the server committed is no longer auto-retried into a duplicate entity. Override via the `configureResilience` hook / `WithResilience(...)` if you need different semantics.

### API refinements (breaking vs `9.0.0-beta-1`)

Beta-to-beta breaking changes — the compiler surfaces each:

- **`LanguageVariantModel.Elements`** is now `IReadOnlyList<BaseElement>` (was `IReadOnlyList<object>` holding raw `JsonElement`s). A fetched variant's elements now feed straight back into `UpsertLanguageVariantAsync` without a cast or reshape.
- **Method names aligned to convention.** `Modify…` now consistently means PATCH, so the two PUT-based ones became `Update…`: `ModifyPreviewConfigurationAsync` → `UpdatePreviewConfigurationAsync`, `ModifyUsersRolesAsync` → `UpdateUserRolesAsync`. `ModifyCollectionAsync` → `ModifyCollectionsAsync` (plural, matching the whole-set PATCH shape of `ModifyAssetFoldersAsync`). `ListCollectionsAsync` → `GetCollectionsAsync` (`List…` is reserved for materialized `IReadOnlyList` results; `Get…` returns the envelope model, as with `GetAssetFoldersAsync`). Two parameter names were also tidied: `folder` → `folders` on `CreateAssetFoldersAsync`, and `scheduleModel` → `schedule` on the publishing methods.
- **Patch property-name enums renamed** to disambiguate the four identically-named `PropertyName` enums: `CollectionPropertyName`, `CustomAppPropertyName`, `SpacePropertyName`, `TaxonomyGroupPropertyName`. The JSON wire value is unchanged.
- **Asset-folder patch models** gained the `*PatchModel` suffix every other domain uses: `AssetFolderAddIntoModel` / `AssetFolderRemoveModel` / `AssetFolderRenameModel` → `…PatchModel`.
- **Clearer deserialization errors.** Malformed values now surface as `JsonException` carrying the offending value (not a bare `FormatException`); envelope-read failures name the failing element; and enums no longer accept numeric JSON input (Management API enums are string tokens).

### Installation

```bash
dotnet add package Kontent.Ai.Management --prerelease
```

### Requirements

- .NET 8.0
- A Kontent.ai environment with Management API v2 access (a Management API key)

## 9.0.0-beta-1 (2026-06-26)  _(prerelease)_

First public beta of the **ground-up modernized Management SDK**, targeting the [Management API v2](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/). This is a major, breaking rewrite of the `8.x` line: a [Refit](https://github.com/reactiveui/refit)-backed transport, `System.Text.Json` serialization, a result-based return type instead of thrown exceptions for API errors, materialized listings, immutable record DTOs, and two new entry points (DI registration and a fluent builder) alongside the existing constructor.

> [!WARNING]
> This is a **prerelease**. Install it with `--prerelease` — without that flag you get the stable `8.x` API, which these notes do **not** describe. Breaking changes may still land between prereleases until the first stable `9.x` ships; pin an exact version if you need stability during the beta. For production today, stay on the latest stable `8.x`.

> [!IMPORTANT]
> **Upgrading from `8.x`?** Read the [upgrade guide](upgrade-guide.md) before you start — it covers every breaking change with before/after examples. The sections below are a summary.

### Highlights

- **Result pattern instead of exceptions.** Methods no longer throw `ManagementException` on `4xx`/`5xx`. Every call returns `IManagementResult` / `IManagementResult<T>` — inspect `IsSuccess`, `Value`, `Error`, `StatusCode`, `RequestUrl`. Opt back into throwing with `EnsureSuccess()`, or use `TryGetValue(out var value)`. Branch on specific failures via the `ManagementErrorCodes` catalog. Only cancellation, programmer errors and invalid configuration still throw — a transport failure or an unreadable response body is a failed result like any other.
- **Three ways to create a client.** The `new ManagementClient(options)` constructor still works (now `IDisposable` / `IAsyncDisposable` — `await using` it). New: `services.AddManagementClient(...)` for DI (with keyed/named clients via `IManagementClientFactory`) and a fluent `ManagementClientBuilder` for non-DI customization.
- **Materialized listings.** `List…Async` walks every continuation page, merges them, and returns the whole set in one result (all-or-nothing — a failed page short-circuits, never a silently truncated set). Large listings (content items, assets, items-with-variants) also expose a streaming `Enumerate…PagesAsync` that yields one page at a time and lets you stop early.
- **Immutable, strongly-typed models.** Generated models are records; an element property *is* its value (`string Title`, `decimal? Price`, `IEnumerable<Reference>` for linked items) or a small companion record — `RichTextValue`, `DateTimeValue`, `UrlSlugValue`, `CustomValue` — each with an implicit conversion for the common case. Edit with a `with` expression. Date/time properties take a `DateTimeOffset` (stored as a UTC instant).
- **Typed element authoring without a generated model.** `ElementBuilder` and `dynamic[]` are gone. Build a typed `IReadOnlyList<BaseElement>` (one record per element kind), with `DynamicElement` as the escape hatch for unmodeled kinds or raw JSON.
- **`RichTextBuilder`.** Composes rich-text HTML and keeps the inline `<object>` placeholders and the `components` array in sync — it mints the shared GUIDs for you.
- **Built-in resilience.** A `Microsoft.Extensions.Http.Resilience` (Polly) pipeline is on by default — retries on transient failures and `429`, exponential backoff with jitter, `Retry-After` handling. Set `EnableResilience = false` to opt out, or replace it via the `configureResilience` hook / `WithResilience(...)`. Note: unlike Delivery/Sync, there is **no default per-attempt timeout** (uploads can run long); add one if you need it.
- **Streamlined asset creation.** `AssetCreateModel` is no longer generic; a one-call `CreateAssetAsync(FileContentSource, fileReference => …)` extension uploads the file and builds the asset around the resulting reference. The matching `UpsertAssetAsync` overload does create-or-update.

### Breaking changes (from `8.x`)

All detailed in the [upgrade guide](upgrade-guide.md); the ones you're most likely to hit:

- **Error handling** → result pattern ([§2](upgrade-guide.md#2-response-handling-exceptions--result-pattern)).
- **Serialization** → `System.Text.Json`; Newtonsoft is gone. Custom Newtonsoft converters and `[JsonProperty]` against SDK models no longer apply ([§8](upgrade-guide.md#8-serialization-newtonsoft--systemtextjson)).
- **Listings** → materialized `List…Async` / streaming `Enumerate…PagesAsync`; the `IListingResponseModel<T>` paging surface is gone ([§3](upgrade-guide.md#3-listings-manual-paging--materialized-results)).
- **Strongly-typed models** → immutable records; element properties are values / `*Value` records, not mutable element wrappers ([§4](upgrade-guide.md#4-language-variants-and-strongly-typed-models)).
- **Untyped authoring** → typed `BaseElement` records; `ElementBuilder.GetElementsAsDynamic(...)` and `dynamic[]` removed ([§5](upgrade-guide.md#5-authoring-elements-without-a-generated-model)).
- **Assets** → non-generic `AssetCreateModel`; the two asset-reference types collapsed onto `AssetReference` (a rendition is a `RenditionReference`; `Renditions = null` keeps them, `[]` removes them) ([§7](upgrade-guide.md#7-assets)).
- **DTO contracts** → widespread `required` members, nullability corrections, and a focused set of renames/retypes to match the Management API v2 wire contract — including response/request collections standardized on `IReadOnlyList<T>` and the environment id typed as `Guid`. The compiler surfaces each one ([§10](upgrade-guide.md#10-model-and-dto-changes)).

### Removed

| Removed | Replacement |
|---------|-------------|
| `ManagementException` as control flow | Result pattern; `EnsureSuccess()` opts back into throwing |
| `ElementBuilder` / `GetElementsAsDynamic(...)` / `dynamic[]` | Typed `IReadOnlyList<BaseElement>`; `DynamicElement` |
| `IListingResponseModel<T>` paging | `List…Async` + `Enumerate…PagesAsync` |
| Generic `AssetCreateModel<T>` | Non-generic `AssetCreateModel` |
| Newtonsoft.Json | `System.Text.Json` |
| Web Spotlight activation (`ActivateWebSpotlightAsync` & co., `WebSpotlightModel`) | Live preview (preview-configuration endpoints); the Spaces `root_item` field remains |
| `Kontent.Ai.Management.Helpers` (`EditLinkBuilder`, …) | No replacement — keep equivalent edit-link logic in application code |
| Legacy hand-rolled HTTP (`ActionInvoker`, `ManagementHttpClient`, `EndpointUrlBuilder`) | Refit transport + resilience pipeline |

### Installation

```bash
dotnet add package Kontent.Ai.Management --prerelease
```

### Requirements

- .NET 8.0
- A Kontent.ai environment with Management API v2 access (a Management API key)

## 8.3.0 (2026-06-05)

### Features
#### Generic `root_item` on Spaces

Spaces now expose a feature-neutral `root_item` reference that replaces the Web Spotlight–specific `web_spotlight_root_item`. The new `RootItem` property is available on `SpaceModel` and `SpaceCreateModel`, and a matching `RootItem` value has been added to the space PATCH `PropertyName` enum.

The old `WebSpotlightRootItem` members still work and are now marked `[Obsolete]`. They will be removed in the next major version — migrate to `RootItem` when convenient.

Back-end semantics:
- **On create:** if both are set, `root_item` wins (`root_item ?? web_spotlight_root_item`).
- **On response:** both fields are returned and carry the same value.
- **On PATCH:** the property path accepts both `root_item` and `web_spotlight_root_item`, targeting the same root item.

##### Example

```csharp
var createModel = new SpaceCreateModel
{
    Codename = "my_space",
    Name = "My space",
    RootItem = Reference.ById(new Guid("1024356f-858f-421a-b804-07c6bfe10ce5"))
};

var space = await client.CreateSpaceAsync(createModel);

// Updating the root item via PATCH
var changes = new[]
{
    new SpaceOperationReplaceModel
    {
        PropertyName = PropertyName.RootItem,
        Value = Reference.ById(new Guid("1024356f-858f-421a-b804-07c6bfe10ce5"))
    }
};

await client.ModifySpaceAsync(Reference.ByCodename("my_space"), changes);
```

### Deprecations
#### Web Spotlight sunset

Web Spotlight has been sunset and replaced by [Live Preview](https://kontent.ai/learn/docs/preview). The three Web Spotlight endpoints still exist and remain callable for backward compatibility, but the Management API now treats them as no-ops that return a static deprecated status (`enabled: false`, `root_type: null`).

The corresponding client methods are now marked `[Obsolete]` so consumers get a compile-time nudge toward Live Preview:

- `ActivateWebSpotlightAsync`
- `DeactivateWebSpotlightAsync`
- `GetWebSpotlightStatusAsync`

`SpaceModel.WebSpotlightRootItem`, `SpaceCreateModel.WebSpotlightRootItem`, and `PropertyName.WebSpotlightRootItem` are likewise marked `[Obsolete]` in favor of the new `RootItem` members.

### Notes

- No breaking changes in this release — all changes are additive or deprecations. No routes changed and no obsolete members were removed.
- Obsolete members continue to function and will be removed in the next major version.

### What's Changed
* En 713 filter by component types by @hejtmii in https://github.com/kontent-ai/management-sdk-net/pull/293
* webspotlight sunset: set obsolete attributes and add RootItem property by @damesene in https://github.com/kontent-ai/management-sdk-net/pull/295

### New Contributors
* @hejtmii made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/293
* @damesene made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/295

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/8.1.0...8.3.0

## 8.1.0 (2026-02-11)

### Features
#### Extended items-with-variant filtering

The `FilterItemsWithVariantsAsync` request now supports additional optional filters under `filters`:

- `spaces`
- `collections`
- `publishing_states` (`published`, `unpublished`, `not_published_yet`)

Example:

```csharp
var request = new ItemWithVariantFilterRequestModel
{
    Filters = new VariantFilterFiltersModel
    {
        Language = Reference.ByCodename("en-US"),
        Spaces = new[] { Reference.ByCodename("default") },
        Collections = new[] { Reference.ByCodename("default") },
        PublishingStates = new[]
        {
            VariantFilterPublishingState.Published,
            VariantFilterPublishingState.NotPublishedYet
        }
    }
};
```

### Notes

- No breaking changes in this release.
- Filter arrays should be omitted/null when unused; empty arrays are rejected by the API.


### What's Changed
* EN-648 - Add spaces, collections, and publishing states filters to variant filter model by @winklertomas in https://github.com/kontent-ai/management-sdk-net/pull/292


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/8.0.0...8.1.0

## 8.0.0 (2026-02-09)

### Features
#### ✨ Variant filtering is now production-ready (items-with-variant)

The early-access variant filtering feature introduced in 6.8.0 has been replaced with its production equivalent.

This release introduces two new endpoints (and SDK methods) under the standard API surface:

- Filter variant references: `POST /v2/projects/{environmentId}/items-with-variant/filter`
- Bulk fetch items with variants: `POST /v2/projects/{environmentId}/items-with-variant/bulk-get`

> [!IMPORTANT]  
> The `EarlyAccess` client has been removed (breaking change). See the **Breaking changes** section for migration guidance.

##### 🔍 Filtering behavior

All filters are optional. They can be omitted or set to null. Empty arrays are not valid input.

The filter endpoint returns **item + language reference pairs** (not the full language variant content). Use the bulk-get endpoint to retrieve the actual items and (when available) their language variants.

##### 📄 Supported filters

The filter request supports the following properties (under `filters`):

- `search_phrase` – searches item names and element values in language variants.
- `language` – operates on a single language. Defaults to the project’s default language if unspecified.
- `content_types` – filters by content type.
- `contributors` – filters by contributor(s).
- `has_no_contributors` – when set to true, the `contributors` filter must be null or omitted.
- `completion_statuses` – supported values: `unfinished`, `ready`, `not_translated`, `all_done`.
- `workflow_steps` – filters by workflow / workflow step(s).
- `taxonomy_groups` – filters by taxonomy group + term(s).

Ordering (under `order`):

- `order.by` – valid values: `name`, `due_date`, `last_modified`.
- `order.direction` – valid values: `asc`, `desc`.

##### 📦 Bulk-get request body

Bulk-get is a POST request because it accepts a list of identifiers in the request body.

The body is:

- `variants`: array of `{ item, language }` identifier pairs
- both `item` and `language` are standard Kontent.ai `Reference` objects (e.g., `{ "id": "..." }` or `{ "codename": "..." }`)

##### 💡 Example usage

###### Minimal request (filter)

```csharp
var request = new ItemWithVariantFilterRequestModel
{
    Filters = new VariantFilterFiltersModel
    {
        Language = Reference.ByCodename("en-US")
    }
};

var response = await client.FilterItemsWithVariantsAsync(request);
```

This request retrieves all English variant references using the default ordering.

###### Filter → bulk-get (fetch items and variants)

```csharp
var filterRequest = new ItemWithVariantFilterRequestModel
{
    Filters = new VariantFilterFiltersModel
    {
        SearchPhrase = "test",
        Language = Reference.ByCodename("en-US"),
        ContentTypes = new List<Reference> { Reference.ByCodename("article") },
        CompletionStatuses = new List<VariantFilterCompletionStatus> { VariantFilterCompletionStatus.Ready }
    },
    Order = new VariantFilterOrderModel
    {
        By = "name",
        Direction = VariantFilterOrderDirection.Ascending
    }
};

var filterResponse = await client.FilterItemsWithVariantsAsync(filterRequest);

var bulkGetRequest = new ItemWithVariantBulkGetRequestModel
{
    Variants = filterResponse.Select(x => new VariantIdentifierModel
    {
        Item = x.Item,
        Language = x.Language
    })
};

var bulkGetResponse = await client.BulkGetItemsWithVariantsAsync(bulkGetRequest);
```

This flow searches for matching variants and then resolves them into content items and their language variants.

> [!NOTE]  
> A returned item may not contain the `variant` property if that specific item+language variant does not exist (or is not accessible). The SDK models `variant` as optional.

### Breaking changes

#### ❌ Removal of `EarlyAccess` client

The `IManagementClientEarlyAccess` interface and `IManagementClient.EarlyAccess` property have been removed.

##### Migration

- Old (6.8.0–7.x):
  - `client.EarlyAccess.FilterVariantsAsync(new VariantFilterRequestModel { ... })`
- New (8.0.0):
  - `client.FilterItemsWithVariantsAsync(new ItemWithVariantFilterRequestModel { ... })`
  - optionally follow with `client.BulkGetItemsWithVariantsAsync(...)` to retrieve full items/variants

#### ❌ Removal of `include_content`

The early-access request supported `include_content` to inline variant elements in the filter response.  
In 8.0.0, **filtering and content retrieval are separated**:

- `FilterItemsWithVariantsAsync` returns **references** (item + language)
- `BulkGetItemsWithVariantsAsync` returns **content items** and their **variants** (when available)

### What's Changed
* EN-649 - Replace early-access variant filter with items-with-variant endpoints by @winklertomas in https://github.com/kontent-ai/management-sdk-net/pull/291


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/7.0.2...8.0.0

## 7.0.2 (2026-02-04)

### What's Changed
* Add display_mode to custom apps by @JiriLojda in https://github.com/kontent-ai/management-sdk-net/pull/290

### New Contributors
* @JiriLojda made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/290

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/7.0.1...7.0.2

## 7.0.1 (2025-12-04)

### What's Changed
* EN-615 - make taxonomy terms and nested asset folders optional by @winklertomas in https://github.com/kontent-ai/management-sdk-net/pull/289


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/7.0.0...7.0.1

## 7.0.0 (2025-11-14)

### Breaking changes
- Legacy webhooks are no longer supported in the product and therefore were removed from the SDK. Details on current webhooks and how to work with them can be found in [related Kontent.ai docs](https://kontent.ai/learn/docs/webhooks/webhooks/net).

### Related PRs
* EN-628 remove legacy webhooks by @sevcik-martin in https://github.com/kontent-ai/management-sdk-net/pull/288


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.9.1...7.0.0

## 6.9.1 (2025-11-05)

- fixes a bug where pagination for early access filtering endpoint failed, due to invalid http method (GET instead of POST)

### What's Changed
* Fix filtering pagination method by @pokornyd in https://github.com/kontent-ai/management-sdk-net/pull/287


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.9.0...6.9.1

## 6.9.0 (2025-09-25)

> [!WARNING]  
> This release includes a minor, yet breaking change in an early access feature. Due to the beta status of the feature in question, this warrants only a minor release.

### What's changed
- `VariantFilterCompletionStatus.Completed` enum member renamed to `VariantFilterCompletionStatus.Ready`

### Related PR
* EN-599 Rename completion status completed to ready by @huluvu21 in https://github.com/kontent-ai/management-sdk-net/pull/285


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.8.0...6.9.0

## 6.8.0 (2025-09-02)

### Features
#### ✨ New Early-Access Endpoint

Introduced a new filtering endpoint, available as early access.

> [!WARNING]  
> As an early-access feature, functionality may change in future releases.

##### 🔍 Filtering Behavior

All filters are optional. They can be omitted or set to null. Empty arrays are not valid input.

##### 📄 Supported Filters

- search_phrase – searches item names and element values in language variants.
- language – operates on a single language. Defaults to the project’s default language if unspecified.
- has_no_contributors – when set to true, the contributors filter must be null or omitted.
- completion_statuses – supported values: unfinished, completed, not_translated, all_done
- order.by – valid values: name, due_date, last_modified.
- order.direction – valid values: asc, desc.
- include_content – optional boolean (default: false). When set to true, the response includes populated variant elements. When false (or omitted), the elements property is not returned.

##### 💡 Example Usage
###### Minimal request
```csharp
var request = new VariantFilterRequestModel
{
    Filters = new VariantFilterFiltersModel
    {
        Language = Reference.ByCodename("en-US")
    }
};

var response = await client.EarlyAccess.FilterVariantsAsync(request);
```


This request retrieves all English language variants using the default ordering.

###### Rich request

```csharp
var request = new VariantFilterRequestModel
{
    Filters = new VariantFilterFiltersModel
    {
        SearchPhrase = "test",
        Language = Reference.ByCodename("en-US"),
        ContentTypes = new List<Reference>
        {
            Reference.ByCodename("article")
        },
        CompletionStatuses = new List<VariantFilterCompletionStatus> 
        { 
            VariantFilterCompletionStatus.Completed 
        }
    },
    Order = new VariantFilterOrderModel
    {
        By = "name",
        Direction = VariantFilterOrderDirection.Ascending
    },
    IncludeContent = true
};

var response = await client.EarlyAccess.FilterVariantsAsync(request);
```


This request searches for completed English articles containing the word test, ordered by name ascending, including content in the response.

### What's Changed
* EN-542 Add early access support for new MAPI endpoint `/early-access/… by @arguit in https://github.com/kontent-ai/management-sdk-net/pull/284


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.7.0...6.8.0

## 6.7.0 (2025-06-12)

### `ElementModelProvider` Class Made Public

- exposes `ElementModelProvider` class, providing `GetStronglyTypedElements<T>()` and `GetDynamicElements<T>()` functions for converting between dynamic and strongly typed element models

### Custom `HttpClient` injection

- overloaded constructor for `ManagementClient` allows specifying your own implementation of `HttpClient` via second constructor parameter

### Dynamic Conversion Fixes

- added missing to/from dynamic conversion methods for all `Reference` type values, and for `AssetWithRenditionsReference`

### What's Changed
* Add support for custom HTTP client, make ElementModelProvider public by @pokornyd in https://github.com/kontent-ai/management-sdk-net/pull/282
* Fix dynamic conversions by @pokornyd in https://github.com/kontent-ai/management-sdk-net/pull/283


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.6.1...6.7.0

## 6.6.1 (2025-04-25)

### Fixes
- fixes #280 by adding missing decorators for `LanguageVariantUpsertModel` serialization

### What's Changed
* add missing decorators to LanguageVariantUpsertModel by @pokornyd in https://github.com/kontent-ai/management-sdk-net/pull/281


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.6.0...6.6.1

## 6.6.0 (2025-02-11)

### Features
* You can now create notes and assign contributors and due date to a language variant via upsert endpoint, more info [in the changelog](https://kontent.ai/learn/product-updates#a-contributors-and-note-support-in-management-api)
* Support for scheduling publishing and unpublishing in one go added, more info [in the changelog](https://kontent.ai/learn/product-updates#a-improved-scheduled-publishing-experience)

### Related pull requests
* EN-376 Add contributors and note to language variants by @huluvu21 in https://github.com/kontent-ai/management-sdk-net/pull/279
* EN-261 Add schedule publish and unpublish endpoint to Management API SDK by @mrnustik in https://github.com/kontent-ai/management-sdk-net/pull/275

### New Contributors
* @mrnustik made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/275

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.5.0...6.6.0

## 6.5.0 (2024-12-11)

### Main features
* Adds support for custom apps, allowing you to integrate a tool of your choice with Kontent.ai interface. See https://kontent.ai/learn/docs/custom-apps

### What's Changed
* Add strongly typed response for listing variants by type by @DiegoFaFe in https://github.com/kontent-ai/management-sdk-net/pull/271
* add name property to taxonomy element by @pokornyd in https://github.com/kontent-ai/management-sdk-net/pull/276
* EN-326 Add Custom Apps support via Management Api by @arguit in https://github.com/kontent-ai/management-sdk-net/pull/278

### New Contributors
* @DiegoFaFe made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/271

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.4.0...6.5.0

## 6.4.0 (2024-08-08)

### What's Changed
* EN-207 Add asset folder codenames by @matus12 in https://github.com/kontent-ai/management-sdk-net/pull/274


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.3.0...6.4.0

## 6.3.0 (2024-07-30)

### New features
- Web spotlight can now be activated/deactivated through management API, optionally allowing you to specify an existing type to use as WSL root. Current status and root type can also be retrieved via a `GET` request. More info in the [changelog](https://kontent.ai/learn/product-updates#a-manage-web-spotlight-via-api).

### What's Changed
* EN-238 Add WebSpotlight support via Management Api by @arguit in https://github.com/kontent-ai/management-sdk-net/pull/273


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.2.0...6.3.0

## 6.2.0 (2024-06-10)

### New features
- you can now select whether to include items and assets, and/or version history when cloning an environment using new `CopyDataOptions` property on `EnvironmentCloneModel`, more info in our [changelog](https://kontent.ai/learn/product-updates#a-create-environments-with-out-content)

### What's Changed
* EN-199 Add options for selecting entities to copy when cloning enviro… by @matus12 in https://github.com/kontent-ai/management-sdk-net/pull/269

### New Contributors
* @matus12 made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/269

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.1.0...6.2.0

## 6.1.0 (2024-05-13)

### New features
* implements recently introduced MAPI endpoints to retrieve published language variants, more info in the [product changelog](https://kontent.ai/learn/product-updates#a-get-published-variants-with-management-api)

### What's Changed
* EN-137 - Add access to published content by @VladimirO-kontent in https://github.com/kontent-ai/management-sdk-net/pull/264


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/6.0.0...6.1.0

## 6.0.0 (2024-04-08)

### Breaking changes
- `projectId` is now `environmentId` in majority of scenarios
- most references to **projects** were updated to refer to **environments**
  - methods such as `withProjectId` are now `withEnvironmentId`
  - properties, comments, documentation...
  - actions that are still project-wide remain unchanged

### Features
##### [Set due dates](https://kontent.ai/learn/product-updates#a-set-due-dates-via-api)
##### [Reorder elements in types and taxonomies](https://kontent.ai/learn/product-updates#a-reorder-elements-and-taxonomies-through-api)
##### [View scheduled publish/unpublish](https://kontent.ai/learn/product-updates?show=apis_sdks#a-view-scheduled-publish-and-unpublish-times-via-api)

### What's Changed
* Project to environment by @pokornyd in https://github.com/kontent-ai/management-sdk-net/pull/259
* Add move operation to type, snippet and taxonomy PATCH by @VladimirO-kontent in https://github.com/kontent-ai/management-sdk-net/pull/260
* EN-130 - Add schedules to content item variant by @VladimirO-kontent in https://github.com/kontent-ai/management-sdk-net/pull/261
* EN-125 Add due date to content item variant by @arguit in https://github.com/kontent-ai/management-sdk-net/pull/262

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/259
* @arguit made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/262

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/5.2.0...6.0.0

## 5.2.0 (2024-02-27)

### What's Changed
* Add custom headers to webhooks by @lenkaklimcikova in https://github.com/kontent-ai/management-sdk-net/pull/258
  * more info on custom webhook headers in Kontent.ai [documentation](https://kontent.ai/learn/docs/webhooks/webhooks/net#a-custom-headers)


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/5.1.0...5.2.0

## 5.1.0 (2024-02-05)

### What's Changed
* #255 add collections to spaces by @AdamZ-Kontent in https://github.com/kontent-ai/management-sdk-net/pull/256
  * collections can now be assigned to spaces, more info [in our documentation](https://kontent.ai/learn/docs/spaces#a-connect-spaces-to-collections)
* Add new webhook filters by @winklertomas in https://github.com/kontent-ai/management-sdk-net/pull/257
  * new filtering options have been added to the reworked webhooks, introduced in the latest major version see [product updates](https://kontent.ai/learn/product-updates#a-extended-webhook-filters) for more information


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/5.0.0...5.1.0

## 5.0.0 (2024-01-15)

- latest major release brings reworked webhook functionality, while keeping support for their legacy implementation, with related methods renamed to clearly communicate their deprecation
- package now targets .NET 8

### Breaking changes.
- all original methods pertaining to legacy webhook functionality have been renamed accordingly
  - `PostWebhook` → `PostLegacyWebhook`
  - `DeleteWebhook` → `DeleteLegacyWebhook`
  - etc.
  - ⚠ **original method names have been retained for the updated webhook functionality implementation**
- ⚠ **.NET 8 was adopted as the latest LTS release, replacing .NET 6 as the supported version**

### New features
#### Improved webhook experience
- webhook methods were adjusted to bring you all the benefits of our reworked webhook experience, allowing more granular trigger event configuration and providing wider range of objects to fire an event from <[Product Update](https://kontent.ai/learn/product-updates#a-easily-track-content-changes-with-new-webhooks) | [Readme](https://kontent.ai/learn/docs/webhooks/webhooks/javascript)>
-  added related tests and code samples



### What's Changed
* Add new webhooks and rename legacy webhooks by @lenkaklimcikova in https://github.com/kontent-ai/management-sdk-net/pull/254

### New Contributors
* @lenkaklimcikova made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/254

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.7.0...5.0.0

## 4.7.0 (2023-09-13)

### What's Changed
* Add asset codename by @VladimirO-kontent in https://github.com/kontent-ai/management-sdk-net/pull/250

### New Contributors
* @VladimirO-kontent made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/250

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.6.0...4.7.0

## 4.6.0 (2023-06-22)

### What's Changed
* New unit tests by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/175
* Fix dead link by @jankalfus in https://github.com/kontent-ai/management-sdk-net/pull/243
* Fix dead link by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/244
* Update workflow response model by @PetrSvirak in https://github.com/kontent-ai/management-sdk-net/pull/246
* #245 Add collection property to asset models by @AdamZ-Kontent in https://github.com/kontent-ai/management-sdk-net/pull/248

### New Contributors
* @jankalfus made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/243
* @PetrSvirak made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/246

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.5.0...4.6.0

## 4.5.0 (2023-04-06)

### What's Changed
* #220 Add support for workflows scoped to collections. by @Iorethan in https://github.com/kontent-ai/management-sdk-net/pull/223
* #233 Add root item to Spaces by @AdamZ-Kontent in https://github.com/kontent-ai/management-sdk-net/pull/234


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.4.0...4.5.0

## 4.4.0 (2023-02-02)

### What's Changed
* 210 introduce GetKontentElementCodename extension method by @Sevitas in https://github.com/kontent-ai/management-sdk-net/pull/217
* fix retry policy by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/213
* Add support for X-KC-SDKID by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/222
* #215 Add support for spaces by @AdamZ-Kontent in https://github.com/kontent-ai/management-sdk-net/pull/221
* #216 - Add support for managing Preview configuration via MAPI by @robertstebel in https://github.com/kontent-ai/management-sdk-net/pull/218


**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.3.0...4.4.0

## 4.3.0 (2023-01-05)

### What's Changed
* Add support for displayTimeZone of DateTime element by @Radimersky in https://github.com/kontent-ai/management-sdk-net/pull/204
* upgrade github actions by @Sevitas in https://github.com/kontent-ai/management-sdk-net/pull/206
* KCL-9904 Remove is_non_localizable property from unsupported type elements in MAPI by @vladbulyukhin in https://github.com/kontent-ai/management-sdk-net/pull/209
* Replace GetExecutingAssembly as it is not supported by self-contained… by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/208

### New Contributors
* @Radimersky made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/204
* @vladbulyukhin made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/209

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.2.0...4.2.1

## 4.2.0 (2022-09-15)

### What's Changed
* Code samples for validation endpoint by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/199
* 201 - support for_default values in modular content and asset elements by @Iorethan in https://github.com/kontent-ai/management-sdk-net/pull/202
* KCL-9407 - add support for H5 and H6 by @Tomas-Kristof in https://github.com/kontent-ai/management-sdk-net/pull/205

### New Contributors
* @Iorethan made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/202
* @Tomas-Kristof made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/205

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.1.0...4.2.0

## 4.1.0 (2022-08-18)

### What's Changed
* Add support for new async validation endpoints in https://github.com/kontent-ai/management-sdk-net/pull/190
* Replace image in tests in https://github.com/kontent-ai/management-sdk-net/pull/198

### New Contributors
* @huluvu21 made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/190

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/4.0.0...4.1.0

## 4.0.0 (2022-08-03)

### What's Changed

* Bug/185 Fix creating url enable disable webhooks by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/186
* Bump Newtonsoft.Json from 12.0.3 to 13.0.1 in /Kentico.Kontent.Management by @dependabot in https://github.com/kontent-ai/management-sdk-net/pull/184
* Update other packages packages by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/193
*  **💥 Breaking change!** Migrate to Kontent.ai brand by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/187
    * Changing package name from `Kentico.Kontent.Management` to `Kontent.Ai.Management`
    * Changing `Kentico.Kontent.*` namespaces to `Kontent.Ai.*`
    * Projects metadata (mainly new company name) adjustments by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/189
        * Set projects' readme and icon by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/191
* **💥 Breaking change!** Target only .NET 6 by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/188
    * Unify back the release process by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/192

From version `3.0.1-3.0.4`

> * #168 - fixed passing second parameter to the request for ChangeLangua… https://github.com/Kentico/kontent-management-sdk-net/pull/169
> * KCL-8688 - Add non-localizable property to the element data model in https://github.com/Kentico/kontent-management-sdk-net/pull/170
> * Feature/allowed link types in https://github.com/Kentico/kontent-management-sdk-net/pull/179
> * 39_retry_after in https://github.com/Kentico/kontent-management-sdk-net/pull/177
> * [#159] - Insert and Update workflow SDK support in https://github.com/Kentico/kontent-management-sdk-net/pull/173
> * expose ToElement method from ElementModelProvider by @winklertomas in https://github.com/Kentico/kontent-management-sdk-net/pull/180
> * 181 - add support for reading the default values for the supported el… by @robertstebel in https://github.com/Kentico/kontent-management-sdk-net/pull/182

### New Contributors

* @winklertomas made their first contribution in https://github.com/Kentico/kontent-management-sdk-net/pull/180
* @AdamZ-Kontent made their first contribution in https://github.com/Kentico/kontent-management-sdk-net/pull/179
* @JanBarton made their first contribution in https://github.com/Kentico/kontent-management-sdk-net/pull/170
* @dependabot made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/184

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/3.0.4...4.0.0

## 4.0.0-beta.5 (2022-08-01)  _(prerelease)_

### What's Changed
* Bug/185 url enable disable webhooks by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/186
* Bump Newtonsoft.Json from 12.0.3 to 13.0.1 in /Kentico.Kontent.Management by @dependabot in https://github.com/kontent-ai/management-sdk-net/pull/184
* Migrate to Kontent.ai brand by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/187
* Target only .NET 6 by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/188
* Projects metadata adjustments by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/189
* Set projects' readme and icon by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/191
* Unify back the release process by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/192
* Update packages by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/193

### New Contributors
* @dependabot made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/184

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/3.0.4...4.0.0-beta.5

## 4.0.0-beta.4 (2022-07-21)  _(prerelease)_

### What's Changed
* Bug/185 url enable disable webhooks by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/186
* Bump Newtonsoft.Json from 12.0.3 to 13.0.1 in /Kentico.Kontent.Management by @dependabot in https://github.com/kontent-ai/management-sdk-net/pull/184
* Migrate to Kontent.ai brand by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/187
* Target only .NET 6 by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/188
* Projects metadata adjustments by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/189
* Set projects' readme and icon by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/191
* Unify back the release process by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/192

### New Contributors
* @dependabot made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/184

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/3.0.4...4.0.0-beta.4

## 4.0.0-beta.3 (2022-07-21)  _(prerelease)_

### What's Changed
* Bug/185 url enable disable webhooks by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/186
* Bump Newtonsoft.Json from 12.0.3 to 13.0.1 in /Kentico.Kontent.Management by @dependabot in https://github.com/kontent-ai/management-sdk-net/pull/184
* Migrate to Kontent.ai brand by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/187
* Target only .NET 6 by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/188
* Projects metadata adjustments by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/189
* Set projects' readme and icon by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/191

### New Contributors
* @dependabot made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/184

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/3.0.4...4.0.0-beta.3

## 4.0.0-beta.2 (2022-07-21)  _(prerelease)_

### What's Changed
* Bug/185 url enable disable webhooks by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/186
* Bump Newtonsoft.Json from 12.0.3 to 13.0.1 in /Kentico.Kontent.Management by @dependabot in https://github.com/kontent-ai/management-sdk-net/pull/184
* Migrate to Kontent.ai brand by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/187
* Target only .NET 6 by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/188
* Projects metadata adjustments by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/189
* Set projects' readme and icon by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/191

### New Contributors
* @dependabot made their first contribution in https://github.com/kontent-ai/management-sdk-net/pull/184

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/3.0.4...4.0.0-beta.2

## 4.0.0-beta.1 (2022-07-20)  _(prerelease)_

### What's Changed
* Bug/185 url enable disable webhooks by @gormal in https://github.com/kontent-ai/management-sdk-net/pull/186
* Bump Newtonsoft.Json from 12.0.3 to 13.0.1 in /Kentico.Kontent.Management by @dependabot in https://github.com/kontent-ai/management-sdk-net/pull/184
* Migrate to Kontent.ai brand by @Simply007 in https://github.com/kontent-ai/management-sdk-net/pull/187

### New Contributors

**Full Changelog**: https://github.com/kontent-ai/management-sdk-net/compare/3.0.4...4.0.0-beta.1

## 3.0.4 (2022-06-30)

### What's Changed
* expose ToElement method from ElementModelProvider by @winklertomas in https://github.com/Kentico/kontent-management-sdk-net/pull/180
* 181 - add support for reading the default values for the supported elements in https://github.com/Kentico/kontent-management-sdk-net/pull/182

### New Contributors
* @winklertomas made their first contribution in https://github.com/Kentico/kontent-management-sdk-net/pull/180

**Full Changelog**: https://github.com/Kentico/kontent-management-sdk-net/compare/3.0.3...3.0.4
