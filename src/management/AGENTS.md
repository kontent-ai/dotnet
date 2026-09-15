# AGENTS.md

Guidance for coding agents working on the **Management SDK**. The root `AGENTS.md` covers everything repo-wide (framework, style, commenting, changelog shape, releasing, branch naming, approval snapshots); this file holds only what is specific to this product.

## Overview

A client for the [Management API v2](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/) — a *write* API: content items, variants, assets, content model, workflows, environment administration. The `9.x` line is a ground-up modernization of `8.x`; the architecture below is settled — treat it as canonical.

- **Result pattern.** Public calls return `IManagementResult<T>` (success flag, value, `IError` with the API's error detail, status code, request URL) instead of throwing on 4xx/5xx. Transport failures are results too, carrying the exception — the one exception is cancellation, which is rethrown so `Task.IsCanceled` and cancellation handlers behave normally. `EnsureSuccess()` / `TryGetValue()` / `AsFailure<T>()` are the opt-in conveniences.
- **Refit transport.** The public `ManagementClient` (partial per domain) wraps the internal `IManagementApi` Refit interface (partial per domain under `Api/`); `ISubscriptionApi` covers the subscription scope. Everything public funnels through `RefitApiResponseExtensions.ToManagementResultAsync` — no endpoint bypasses it, no hand-rolled HTTP.
- **`System.Text.Json`** with a small set of converters encoding real MAPI quirks (polymorphic elements, codename-out/id-in mapping, string-encoded numbers). Newtonsoft is gone; do not reintroduce it.
- **Materialized listings.** Every listing is `List{Plural}Async` → `IManagementResult<IReadOnlyList<T>>`, drained internally via `PageEnumerator` (all-or-nothing: first failed page short-circuits). Unbounded listings additionally expose `List{Plural}PageAsync`, which fetches one page and returns a `ListingPage<T>` with the continuation token.
- **Resilience by default** (`Microsoft.Extensions.Http.Resilience`/Polly), outermost in the handler chain, with **idempotency-aware retries**: 429 retries every method; transient failures/5xx retry idempotent methods only. This is a write API — never weaken that invariant. Auth and tracking are `DelegatingHandler`s under it.
- **One builder, two hosting modes**: `services.AddManagementClient(management => …)` (DI, keyed/named clients, options validation) and `ManagementClient.Create(management => …)`, which runs the same registration in a private container the built client owns. The builder is `IManagementClientBuilder` — `Options`, `HttpClient`, `SubscriptionHttpClient`, `ConfigureResilience`, `Services`, `Name`; the shared implementation is `src/common/Clients`. The plain constructor is `Create(options)` and is disposable.

## Current phase

`eng/Versions.props` is the authority on where this product is; read it rather than trusting a sentence here.

**The window for casual breaking changes is closed.** A break needs a real defect or a clearly better architecture behind it, a `CHANGELOG.md` entry under `## Unreleased`, an entry in the upgrade guide for the major in progress, and an approval-snapshot update. Renaming stable, sensible API purely to modernize naming does not clear the bar — familiarity has value.

## Divergences from the siblings

`src/delivery` is the primary reference, `src/sync` the secondary. Two divergences are deliberate, both because this SDK writes: **no default per-attempt timeout**, and the **idempotency-aware retry rule** above. Anything else that differs from Delivery needs a stated reason.

## API surface conventions

- **Verbs**: `Get` (single or envelope model), `List{Plural}` (materialized `IReadOnlyList`), `List{Plural}Page` (one continuation-token page), `Create` (POST), `Upsert`/`Update` (PUT), `Modify` (PATCH — only PATCH), `Delete`. Parameter order is `(identifier, payload, cancellationToken = default)` everywhere.
- **The interface is one method per API operation** (plus typed `<T>` projections of the same operation). Conveniences that *compose or adapt* — fetched-model→request adapters, multi-call helpers — live in the extensions tier (`Extensions/ManagementClientExtensions` and friends), never on `IManagementClient`.
- **Identifiers**: `Reference` is factory-only (`ById`/`ByCodename`/`ByExternalId`) and stays explicit — no implicit conversions (decided; `ToReference()`/`ToIdentifier()` extensions are the sanctioned ergonomic path). URL segments are left **raw** in `ToUrlSegment` — Refit's `{**}` catch-all percent-encodes exactly once; pre-escaping double-encodes.
- **Patch factories** are the curated way through patch grammars: `ContentTypePatch`/`ContentTypeSnippetPatch` (JSON-pointer paths), `LanguagePatch`/`SpacePatch`/`TaxonomyGroupPatch`/`CustomAppPatch` (property-name enums). Raw operation records remain the escape hatch.
- **Experimental surface** carries `[Experimental("KAIM001")]` (currently the content-model snapshot). New not-yet-contractual features follow the same pattern.

## Model conventions

- Sealed, immutable `record`s; `required` for what the API always returns/demands; nullability mirrors the wire contract exactly ("encode API learnings in the type system, not in prose").
- Explicit `[JsonPropertyName]` on **every** property — the serializer options deliberately have no naming policy.
- **One documented exception to the repo date rule**: `ScheduleResponseModel` exposes its timestamps as `DateTimeOffset` even though the server sends them. The API returns them alongside a separate `display_timezone`, and the pair is what a caller reschedules with, so the offset is carried rather than discarded. Test-pinned; do not "align" it.
- **All collection properties are `IReadOnlyList<T>`** (never `IEnumerable`, `ISet`, or concrete types). Method *parameters* may accept `IEnumerable<T>`.
- Names mirror Kontent.ai API terminology; request and response shapes are separate records when the wire shapes differ (a response model with a fake-`required` field forced into a request body is a defect — see `UserRolesUpdateModel`).
- Models must stay **generator-friendly** (record-based, immutable, STJ-serializable) — the shape is coordinated with `src/model-generator`, which consumes this package at a floor, so a change here reaches it only after a release.
- **Content-type CLR models come from the generator.** Extensions belong in separate partial record files. Generated models map via `ContentTypeAttribute`/`ContentElementAttribute`/`ContentOptionAttribute` under `Annotations/`; the converters read them at runtime (typed *reads* match by id — environment-bound; *writes* key by codename — portable).
- **Asset uploads must carry a `Content-Length`** (verified against the live endpoint). A chunked request is refused with error `206`, *"the file is bigger than the maximal allowed limit (2 GB)"*, whatever the real size — so the message is no guide to the actual fault. A zero-length body, by contrast, is **accepted** and stores an empty asset. `FileContentSource` therefore only takes sources whose size is knowable, which is also what makes every upload safe to retry.

## Adding or changing an endpoint — the playbook

1. **Refit method** in the matching `Api/IManagementApi.{Domain}.cs` partial (`internal`, suffix `InternalAsync`, returns `IApiResponse<T>`; identifiers travel as pre-rendered `{**segment}` catch-all strings).
2. **Implementation** in `ManagementClient.{Domain}.cs`: null-check args, `identifier.ToUrlSegment()`, `.ToManagementResultAsync()`. Listings go through `PageEnumerator.CollectAsync`/`EnumerateAsync`.
3. **Declaration + XML docs** in `IManagementClient.cs` (docs describe the operation and the result; typed overloads cross-reference the environment-bound caveat).
4. **Tests** in `Kontent.Ai.Management.Tests/ManagementClientTests/{Domain}Tests.cs`: MockHttp `Expect` on the exact URL, JSON fixture under `Data/{Domain}/`, `CaptureBody` + `ShouldMatchSerialized` for write bodies, `PagedFixtures.ConcatPages` for listings, null-guard tests.
5. **Approval snapshot** in `Kontent.Ai.Management.Tests/ApiApproval` — review, then copy `.received.txt` over `.verified.txt` only for intended changes.
6. **Docs**: a section in the guide that owns the surface (`docs/configuration.md`, `requests-and-results.md`, `content-items-and-variants.md`, `models.md`, `assets.md`, `content-model.md`, `administration.md`), the changelog entry, and — if breaking — the upgrade guide. *A public-surface change documented nowhere is an incomplete change.* Touch `README.md` only when first-use behaviour or navigation changes — it is a routing page, and adding a row per new method is what turned it into a 993-line manual before.
7. Wire contract in doubt? Verify against the OpenAPI reference or the JS SDK's contracts (`kontent-ai/management-sdk-js`, `lib/models`/`lib/contracts`) — and say what you verified against.

## Testing conventions

- xUnit + `RichardSzalay.MockHttp` + Verify. `Base/MockClientFactory.Create()` runs the SDK's own registration in a private container with the mock as the primary handler, so tests exercise the real Refit + handler chain; resilience is switched off there so a 5xx fixture fails the call instead of being retried. JSON fixtures per domain under `Data/`.
- Inject a scoped `ContentItemEnvelopeConverter` for typed-model tests — auto-scan trips the deliberate test-assembly codename collision.
- **CodeSamples are tests** (`CodeSamples/*.cs`) and double as documentation-grade example code — keep them modern and idiomatic (result handling via `EnsureSuccess()`, identifier factories, patch facades). `CreateForSample`'s fallback returns an empty 200, which maps to a *failure* for value-returning calls — samples that unwrap need a real fixture, and listing fixtures must carry a `null` continuation token or pagination loops forever.
- Wire-level guarantees get explicit serialization tests (e.g. `RequestDefaultsSerializationTests` pins that ergonomic defaults keep the payload byte-identical). Zero-regression on the wire is the standing bar for "ergonomics" changes.
- The attribute-driven wire mapping in `Conversion/` is the sanctioned use of reflection; cache anything reflective.

## Project structure

- `Kontent.Ai.Management/` — `Api/` (Refit partials), `Configuration/` (options, builder, Refit settings), `Extensions/` (DI registration, client conveniences, `PageEnumerator`, result mapping), `Handlers/` (auth, tracking), `Conversion/` + `Serialization/` (typed-model envelope converter, STJ converters), `Annotations/` (generated-model mapping attributes), `Models/` (per-domain DTOs), root-level result types and error catalog (`ManagementErrorCodes` — curated, not exhaustive; MAPI codes are not unique).
- `Kontent.Ai.Management.Tests/` — mirrors the above; `Base/` holds the shared test infrastructure.
- No Abstractions or Helpers project — considered and dropped; public contracts live with the implementation.

## Open questions (do not invent answers)

- **Coordination with `src/model-generator`** for Management-model generation — the generated model shape (records, mapping attributes, collection types) must be agreed jointly before DTOs are declared final.
- **Webhook trigger switches** (`Enabled`/`Events`/`Slot` nullability) and the **webhook update endpoint** need live-API verification before changing.
- **How long a continuation token stays valid.** `ListingPage<T>.ContinuationToken` is what makes an interrupted listing resumable rather than restartable, so the answer decides what the docs may promise. It is undocumented — absent from the MAPI reference and the [API limitations](https://kontent.ai/learn/docs/apis/management-api-v2/api-limitations) page — while the error catalog carries *"The specified continuation token is incorrect"*, so an invalid token is a reachable state.

  The working assumption is that tokens are **long-lived**, because MAPI pages over Cosmos DB and a Cosmos continuation token has no TTL. Two things could still break that, neither observable from the SDK: a MAPI-side layer minting its own tokens over the Cosmos ones, and Cosmos tokens being bound to the query's physical partition layout, so a partition split can invalidate one that never expired in any time sense. Until someone on the API side confirms, the README and `ListingPage<T>` deliberately promise only that a token is dependable across a retry backoff — wording that holds either way. Confirm before documenting cross-process checkpointing.

When you hit these, ask rather than guess.
