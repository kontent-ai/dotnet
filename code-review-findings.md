# Monorepo code review — findings and fix plan

*2026-08-06 · produced by six parallel review agents (one per product + one for shared infra/eng/workflows), judged against the standards in the root and per-product `CLAUDE.md`. Read-only analysis — nothing has been changed or committed.*

## Executive summary

The monorepo is in good health overall: Management and the shared infrastructure came back nearly clean, the release chain's invariants verified correct, and `src/common` follows its own rules to the letter (all internal, no product types, every file consumed). The consequential findings cluster in four places:

1. **One cross-product transport bug** in shared source: `RefitResponses.RethrowIfCanceled` misreports `HttpClient` timeouts as caller cancellation in **all three SDKs** — worst on Management, a write API, where a caller may conclude "nothing was sent" for a request the server may have applied.
2. **A hole in the repo's hardest-leaned-on gate**: the shared public-API approval printer never renders constructors (or events), so constructor breaking changes bypass every product's snapshot gate.
3. **Delivery's cache orchestration**: one skeleton duplicated across six query builders, embedding a genuine data race under FusionCache eager refresh, plus a singleton client that defeats `IHttpClientFactory` handler rotation (stale-DNS pitfall).
4. **Time-boxed breaking-change windows**: Sync's staged 2.0, Management's pre-GA, model generator's 10.x beta, and aspnetcore's 0.x are each the *last cheap moment* for their respective public-surface fixes. These decisions expire; internal cleanup doesn't.

Finding counts: Delivery 24, Management 15, Model generator 34, ASP.NET Core 18, Sync 23, Infra 20. Full per-product detail below; the fix plan at the end sequences everything by severity and deadline.

---

## Cross-cutting findings

These span products and should be fixed once, in the shared source, with all consumers verified.

| ID | Sev | Where | Problem | Fix |
|---|---|---|---|---|
| X1 | **High** | `src/common/Http/RefitResponses.cs:30-37` | `RethrowIfCanceled` rethrows on *any* `OperationCanceledException`, but `HttpClient.Timeout` expiry surfaces as `TaskCanceledException` wrapping `TimeoutException` — a network timeout is misreported as the caller's own cancellation, violating the documented result-pattern contract. Affects Delivery, Management, and Sync (all three compile this file in). The sibling `HttpRetryPredicates.IsTransientException` already makes the correct distinction — the two shared helpers disagree. | Rethrow only when the OCE is not timeout-shaped (skip when `InnerException is TimeoutException`), or thread the caller's token through and rethrow only when `token.IsCancellationRequested`. Coordinate across all three SDKs. |
| X2 | **High** | `src/testing/PublicApiApproval.cs:88-96, 127-141` | The approval printer renders fields, properties, and methods but **never constructors** (confirmed: zero `.ctor` lines in any `.verified.txt`) — public constructor changes bypass every product's API gate. Events are likewise unrendered. | Render `GetConstructors` and `GetEvents` (declared-public, ordinal-sorted), then re-approve all snapshots once. |
| X3 | Medium | `src/testing/PublicApiApproval.cs:117-123` | Init-only properties render as `{ get; set; init; }` — immutable properties presented as mutable in the very diffs reviewers read line by line. | Emit `init;` *instead of* `set;` when the setter carries the `IsExternalInit` modreq. |
| X4 | Low | Delivery `RefitSettingsProvider.cs:15-25`, Management `ServiceCollectionExtensions.HttpClient.cs:44-48`, Sync `ServiceCollectionExtensions.cs:407-414` | The same pattern in all three SDKs: a one-line (or dead) wrapper around `RefitSettingsProvider.CreateDefaultSettings()` — Delivery's is fully dead, Management's and Sync's are pointless indirection (Sync's XML doc also drifted). | Delete/inline in all three. |
| X5 | Low | Delivery ×6 sites, Sync `TrackingHandler.cs:37-40`, generator `ArgHelpers`/`UsedSdkInfo`, Management `FileContentSource.cs:46-47` | Defensive null-guards and throws on internal, non-boundary call paths — against the repo-wide "validate at external boundaries only" rule. | Sweep them out per the per-product lists below. |
| X6 | Low | Sync + Delivery csproj shared-source item groups | Mis-indented `<Compile Include>` lines from appended-not-formatted edits (flagged independently by two agents). | Re-indent. |

---

## Delivery SDK (`src/delivery`) — stable 19.x; internal fixes cheap, public breaks need a major

No critical findings. All three highs are internal.

### High

| ID | Cat | Where | Problem | Fix | Breaks API |
|---|---|---|---|---|---|
| DEL-H1 | arch | `Api/QueryBuilders/ItemQuery.cs:79-181`, `ItemsQuery.cs:119-224`, `TypesQuery.cs:70-124`, `TaxonomiesQuery.cs:64-118`, `TypeQuery.cs`, `TaxonomyQuery.cs` | The cache-orchestration skeleton (closure capture, hit classification, fail-safe probing, next-page fetcher) is duplicated near line-for-line across six query builders (~600–800 lines); any fix must land six times. | Extract a generic internal `CachedQueryExecutor<TResponse>` taking key/fetch/dependencies/wrap as delegates. | No |
| DEL-H2 | bug | `Extensions/ServiceCollectionExtensions.HttpClient.cs:47-53` + `ServiceCollectionExtensions.cs:405,439-465` | Keyed-transient `IDeliveryApi` is resolved once into a keyed-**singleton** `IDeliveryClient`, so the handler chain lives forever and `IHttpClientFactory` rotation never applies — classic stale-DNS pitfall in long-running apps. | `ConfigurePrimaryHttpMessageHandler` with `SocketsHttpHandler { PooledConnectionLifetime = ... }`. | No |
| DEL-H3 | bug | `ItemsQuery.cs:126-145` (+5 siblings) vs `FusionCacheManager.cs:406-409` | `apiResult`/`factoryInvoked` are unsynchronized captured locals written inside the FusionCache factory; with `EagerRefreshThreshold > 0` the factory runs on a background thread — the post-call reads race, and a normal eager-refresh hit is misreported as `ResponseSource.FailSafe`. | Classify hit/fail-safe from state FusionCache exposes, not side-channel locals; fold into the DEL-H1 extraction. | No |

### Medium

| ID | Cat | Where | Problem | Fix | Breaks API |
|---|---|---|---|---|---|
| DEL-M1 | bug | `Abstractions/.../DependencyTrackingContext.cs:93-109` | `IsComponentCodename` heuristic misclassifies real items whose codename's third `_`-segment is 4 chars starting "01" — their cache invalidation dependency key is silently never created (stale content on webhook invalidation). | Identify components structurally from the API response, not by codename pattern. | No |
| DEL-M2 | bug/doc | `HybridCacheManager.cs:8`, `DeliveryClientBuilderExtensions.cs:87,120,158` vs `FusionCacheManager.cs:155-181` | Public docs promise "L1 memory + L2 distributed" hybrid cache; the implementation is deliberately L2-only. Real expectation mismatch. | Decide: fix docs, or wire the L1 tier (FusionCache supports it). | Doc fix: no |
| DEL-M3 | bug | `RichTextParser.cs`, `HtmlResolver.cs`, `DefaultResolvers.cs`, `RichTextExtensions.cs:245` (many awaits) | The rich-text path consistently omits `ConfigureAwait(false)` while the rest of the SDK is diligent — deadlock risk on sync-context hosts. | Add throughout; consider enforcing CA2007 for library projects. | No |
| DEL-M4 | arch/bug | `ContentItems/TypeProvider.cs:32,40-83,114-134` | Process-global `static Lazy` auto-discovery eagerly `Assembly.Load`s all entry-assembly references, with a `GetCallingAssembly` fallback the comment itself concedes only "happens to work in most xUnit setups". | Drop the calling-assembly fallback; consider per-registration discovery. | No |
| DEL-M5 | bug | `HtmlResolver.cs:36-41` + `HtmlResolverBuilder.cs:225-253` | Tag dispatch is reconstructed by parsing the public `description` string (`"Tag="` prefix) — a caller passing `description: "Tag=div"` with an unrelated predicate silently gets tag dispatch that bypasses their predicate. | Carry an explicit internal `TagName` field set only by the tag overload. | No |
| DEL-M6 | arch | `Abstractions/Caching/DeliveryCacheOptions.cs:110` | `ConfigureFusionCacheOptions` is `Action<object>` with docs instructing consumers to cast — the abstraction leaks its implementation with no compile-time safety. | Strongly-typed overload in the Caching package; obsolete the `Action<object>` at next major. | **Yes — defer to major** |
| DEL-M7 | bug | `ServiceCollectionExtensions.cs:467-482` | Shared `JsonSerializerOptions` bridging only detects instance-based registrations; factory-registered options silently produce the exact Refit/mapper divergence the method exists to prevent. Doc also misstates behavior. | Detect any descriptor shape; fail fast or resolve lazily from the provider in both places. | No |
| DEL-M8 | bug | `ElementValueMapper.cs:67-95` | Element deserialization failures are swallowed to a quiet log + `null` property — indistinguishable from a genuinely empty element. | Warning-level log + consider opt-in strict mode mirroring `ThrowOnMissingResolver`. | Additive |

### Low

18 items, abbreviated: dead `RefitSettingsProvider.CreateDefaultSettings` (→X4); misleading `Replace` of identical `IContentDependencyExtractor` registration (`Caching/.../ServiceCollectionExtensions.cs:545`); verbatim-duplicated paging loop (`EnumerateItemsQuery.cs:103-143` vs `DynamicEnumerateItemsQuery.cs:117-157`); pass-through wrappers `MemoryCacheManager`/`HybridCacheManager`; ~100 lines of XML narration on internal `DependencyTrackingContext`; narrating comments in `TrackingHandler.cs:29-36`, `DynamicItemsQuery.cs:8`, `RefitSettingsProvider.cs:40-46`; stale "Encoding is handled by Refit" comment on a security-relevant boundary (`FilterValueSerializer.cs:10`); copy-paste XML docs on `DeliveryOptionsBuilder` mutators (ships to IntelliSense); redundant pattern in `ParseRenditions`; internal defensive null-guards (→X5); wrong exception type for missing `type` in `ContentElementConverter.cs:44` (`KeyNotFoundException` instead of `JsonException`); possible double-`?` URL in `CreateAsset` (`ElementValueMapper.cs:230-236`); over-broad `ICollection<>` surface on `SerializedFilterCollection`; inconsistent null-guarding in `ItemsQuery.ProcessItemsAsync`; internals-in-Abstractions via 4-way `InternalsVisibleTo`; stray empty `Examples/` folder; test nits in `CheckNamespaces.cs`; silent no-op `ImageUrlBuilder.WithFitMode("bogus")`/`WithFormat("bogus")` string overloads (**behavior change — defer to major**).

---

## Management SDK (`src/management`) — 9.0 beta, pre-GA window open

Cleanest product reviewed. One high (the shared X1, manifesting worst here), everything else polish.

| ID | Sev | Cat | Where | Problem | Fix | Breaks API |
|---|---|---|---|---|---|---|
| MGT-H1 | High | bug | = **X1** (via `Extensions/RefitApiResponseExtensions.cs:34,64`) | Timeout-as-cancellation on a *write* API: caller may conclude "nothing was sent" for an applied request. | Fix in `src/common` (X1). | No |
| MGT-M1 | Medium | bug | `Api/ManagementApiFactory.cs:51-54`; DI path never touches `Timeout` | SDK deliberately omits Polly `AddTimeout` so uploads survive, yet leaves the default 100s `HttpClient.Timeout` — which caps *all* retries + backoff and kills long uploads, surfacing (with X1) as bogus cancellation. | `Timeout = Timeout.InfiniteTimeSpan` where the resilience handler owns timing, or document the cap + expose a knob. | No |
| MGT-M2 | Medium | bug | `Models/Assets/FileUploadContent.cs:30-46` | 429-retried upload from a non-seekable caller stream silently re-sends an empty body — and can *succeed* server-side. | Track first-send of non-replayable sources and throw descriptively on replay (or buffer small streams). | No |
| MGT-M3 | Medium | simpl | `Extensions/ServiceCollectionExtensions.cs:324-325` | Unkeyed `IManagementApi`/`ISubscriptionApi` singleton registrations are dead — internal interfaces nothing resolves unkeyed; exist only so tests can assert them. | Delete both + the three test assertions. | No |
| MGT-L1..L11 | Low | — | various | One-line `CreateRefitSettings` wrapper (→X4); unreachable throw in `FileContentSource` (→X5); missing null-validation on three composite identifier constructors (misleading downstream errors); `Component.Content = null!` degrades to bare NRE in `WriteComponent`; custom-app XML docs pluralized for singular ops + grammar slips (`IManagementClient.cs:316,324,333,379,403`); un-modernized 8.x-style `AssetExtensions` docs; `SourceTrackingHeaderAttribute` docs point to retired Kentico branding + dead wiki link; stale test comments (`[Required]` that doesn't exist; history-annotating capture comment); inconsistent patch-factory argument validation; **`DateTime` vs `DateTimeOffset` split on `LastModified` across 8 response models** (decide before GA — breaking); redundant namespace qualification + stray blank line. | Sweep before the GA snapshot freezes. | L10 yes (pre-GA decision); rest no |

---

## Sync SDK (`src/sync`) — 1.0.0 released, **2.0 already staged in changelog: breaking window open now**

| ID | Sev | Cat | Where | Problem | Fix | Breaks API |
|---|---|---|---|---|---|---|
| SYN-H1 | High | bug | `Extensions/RefitApiResponseExtensions.cs:21-85` | Neither `ToSyncResultAsync` overload disposes the `IApiResponse` — every request leaks the response to GC. Management's canonical version wraps in `using (response)`. | Mirror Management. | No |
| SYN-H2 | High | arch | `Abstractions/Models/ISync{Item,Type,Language,Taxonomy}.cs` + 4 internal records + `SyncDeltaResponse.cs:26-29` | Four byte-identical interfaces backed by four byte-identical records plus four explicit re-implementations — 12 files carrying one shape, existing only to feed the Abstractions split. | Replace with public sealed records in the 2.0 window. | Yes (in staged 2.0 break set) |
| SYN-H3 | High | arch | Same files, `Data` property | `Data` is `object?` (a `JsonElement` at runtime) — an opaque box with no doc of the runtime type and no deserialization help; this is the SDK's actual payload. | Type as `JsonElement?` minimum, or provide typed access — in the 2.0 window. | Yes |
| SYN-ARCH | High | arch | whole `Kontent.Ai.Sync.Abstractions` package | The Abstractions split doesn't pull its weight: the package isn't even abstract (`SyncOptions` is concrete), it forces the H2 duplication, and Management already decided against the pattern. **2.0 is the only cheap moment to fold it in** (ship a final type-forwarding shim). | Fold Abstractions into `Kontent.Ai.Sync` in 2.0 — or document keeping it as a deliberate divergence. | Yes |
| SYN-M1..M8 | Medium | — | various | Dead unkeyed `TryAddSingleton<ISyncApi>` registration; `[JsonConverter]` on `ChangeType` silently overriding the snake_case converter (latent multi-word-token failure); Delivery-inherited host-rewrite machinery with unreachable branches; wrong exception type documented on `SyncClientBuilder.Build()`; options-registration block pasted three times; drifted `CreateRefitSettings` doc (→X4); `SnapshotSyncOptionsAccessor` aliases instead of snapshots (name promises isolation the code doesn't provide); `ErrorCode` backfilled with HTTP status codes against its own docs (Management leaves it null). | Per-item; none break signatures. | M2/M8 behavioral only |
| SYN-L1..L12 | Low | — | various | Redundant `GlobalUsings` lines vs `ImplicitUsings`; transposed parameter order between generic `SyncResult` ctor and its factory (silent-swap trap); leftover ctor-boilerplate `<remarks>`; inaccurate `Validate` doc; XML summaries on private helpers; guards + guard-only tests on internal `ComposeSourceHeaderValue` (→X5); redundant `internal` modifiers + missing `InternalAsync` suffix convention; reflection in tests where `HttpMessageInvoker` works; wrong comment + unnecessary reflection in builder test; `// Arrange/Act/Assert` scaffolding drift in exactly two files; vestigial Delivery-copied test name/assertion; csproj indentation (→X6). | Sweep. | No |

---

## Model generator (`src/model-generator`) — 10.x beta; mid-cleanup, one real defect family + a fossilized stratum

### High

| ID | Cat | Where | Problem | Fix | Breaks API |
|---|---|---|---|---|---|
| GEN-H1 | bug | `Core/Common/ClassDefinition.cs:52-66` + `DeliveryCodeGenerator.cs:62-63`, `ManagementCodeGenerator.cs:189-190` | Collision detection runs on raw codenames while emission runs on PascalCased identifiers, and the codename constant registers *before* `AddProperty` can throw — distinct codenames sanitizing to the same identifier (`my_element`/`my__element`, or `title` + `title_codename`) produce **uncompilable generated code**. | Dedupe on sanitized identifiers in one name-space (constants + properties); register the constant only after the property is accepted. | No |
| GEN-H2 | arch | `PartialClassCodeGenerator.cs`, `ClassCodeGeneratorFactory.cs:14,21-24`, `IClassCodeGeneratorFactory.cs:14`, `ClassCodeGenerator.cs:24` | The custom-partial emission path is unreachable from the shipping CLI — generator class, factory flag, fragile `GetType() != typeof(...)` check, and the `DeliveryClassCodeGeneratorBase` split are all vestiges of the dropped feature. | Delete the path; fold the base class into `DeliveryClassCodeGenerator`. | Yes (beta-justified) |
| GEN-H3 | arch | `DeliveryElementService.cs`, `IDeliveryElementService.cs`, `DeliveryCodeGeneratorBase.cs`, `Program.cs:89` | `GetElementType` returns its argument unchanged; injected `Options` never read — interface + implementation + DI registration + inheritance layer computing the identity function. | Delete all of it; use `element.Value.Type` directly. | Yes (beta) |
| GEN-H4 | simpl | `ClassCodeGenerator.cs:26-34,87-92,97-126,156-177` | `IsRecord`/`UseFileScopedNamespace` virtuals are always overridden to `true` — the non-record and block-namespace branches are dead seams from the dropped legacy emission. | Remove both virtuals and their else-branches. | Yes (beta) |

### Medium

GEN-M5..M16, abbreviated: default-less exception switch silently discards unexpected per-element exceptions (`CodeGeneratorBase.cs:77-91`); cross-type filename collisions silently overwrite generated files; vestigial `AggregateException` special case exits with no message (`Program.cs:67-74`); "class was successfully created" logged when nothing was written (`IOutputProvider.Output` should report); management-mode "no content types" message renders an empty environment id (reads only `DeliveryOptions`); five dead public members on `Property`; two dead `TextHelpers` members (one with a lying name — produces `Pascal_Snake_Case`, not upper snake); dead `AddProperty(ref)` overload; `ClassCodeGeneratorFactory` null-checks a logger it never uses; both factory-interface pairs are ceremony for `new`-ing two trivial objects (and `ManagementCodeGenerator` bypasses the factory anyway); dead `CodeGeneratorTestsBase` with drifted doc; five-level generator inheritance that collapses naturally once H2–H4 land.

### Low

GEN-L17..L34, abbreviated: drifted `<see cref>` to renamed method; comment referencing nonexistent `Parse` method + obvious narration; csproj comment contradicting the actual default OutputDir; banned history annotation in `SnippetExpander`; narration comments in emitters; "slice 7" planning-doc reference in a test; **culture-sensitive casing in identifier sanitization** (tr-TR yields different identifiers per machine; Core ships as a library without `InvariantGlobalization`); traversal guard false-positives at filesystem root; decorative generic parameter on `ProgramOptionsData<T>`; redundant `BaseRecord` re-check; undisposed `AdhocWorkspace` ×3; manual array-splice vs collection expression; emitted files carry unused usings (IDE0005 warnings for consumers); sync/async asymmetry on `IUserMessageLogger`; DI lifetime inconsistencies; `Enum.TryParse` accepting out-of-range numerics in arg validation; near-duplicate drifted tests; `CodeGeneratorOptions` doc drift (docs claim Delivery-only + a mode-selection mechanism that doesn't exist).

---

## ASP.NET Core extensions (`src/aspnetcore`) — 0.17.0-preview; breaks cheap

| ID | Sev | Cat | Where | Problem | Fix | Breaks API |
|---|---|---|---|---|---|---|
| ASP-H1 | High | bug | `AssetTagHelper.cs:193-194` (used 122-123) | `Convert.ToDouble(value.ToString())` throws an unhandled `FormatException` (500 at render) for any non-numeric `width`/`height` (`"100%"`, `"auto"`), and parses with **current culture** while `ImageUrlBuilder` formats invariant — asymmetric round-trip on non-invariant servers. | `double.TryParse(..., InvariantCulture, ...)`, `null` on failure → falls through to non-explicit-size path. | No |
| ASP-M2 | Medium | arch | `Webhooks/Models/Reference.cs` + `ReferenceTests.cs` | `Reference` is dead public API — nothing in the package uses it; kept alive only by its own test. | Delete both. | Yes (cheap pre-1.0) |
| ASP-M3 | Medium | bug | `SignatureMiddleware.cs:107-112` | Throwing `UTF8Encoding` guards an impossible scenario (body already decoded with replacement at line 58) and its comment states a false invariant. | Hash the raw request bytes (buffer to `byte[]`, HMAC that) — removes the decode, the dead guard, and the misleading comment in one move. | No |
| ASP-M4 | Medium | bug | `AssetTagHelper.cs:19-22,94-97` | DI-injected `IOptions<ImageTransformationOptions>` property is public/settable and not `[HtmlAttributeNotBound]` — Razor exposes it as a bindable HTML attribute; duplicated by the constructor. | Primary constructor + private field. | Yes (snapshot) |
| ASP-M5 | Medium | simpl | `SignatureMiddleware.cs:14-31` | Middleware exposes its options (containing the secret) as a public property no consumer scenario needs. | Primary constructor, store privately. | Yes (cheap) |
| ASP-M6 | Medium | simpl | `AssetTagHelper.cs:174-183` | Rendition path re-implements `ImageUrlBuilder`'s query-param names by string concatenation (silent drift risk) and blindly appends `?{query}` (double-`?` malformed URL). | Share one helper; ideally extend `ImageUrlBuilder` to accept a raw rendition query. | No |
| ASP-L7..L18 | Low | — | various | Null `Asset` leaks raw `<img-asset>` element into production HTML (should `SuppressOutput`); `RichTextTagHelper` never passes a cancellation token + rebuilds fallback resolver per render; nothing `sealed`, webhook DTOs mutable classes not records; ambiguous `UseWebhookSignatureValidator` overload set on literal `null`; wrong `EnvironmentId` XML doc ("Identifier of the webhook"); nine-line XML remarks on a private method; history-annotating test comment; duplicated `ComputeHmacSha256` test helper; pre-collection-expression syntax; async `ProcessAsync` for sync work; `context.Items` keyed by bare `"sizes"` string; `WebhookItem.LastModified` is `DateTime` for an offset-bearing timestamp. | Sweep; fold DTO→record conversion together. | Several (all cheap pre-1.0) |

---

## Shared infra & eng (`src/common`, `src/testing`, `eng/`, workflows)

**Verified positives:** `src/common` discipline fully holds (all internal, no product types, zero orphaned files); `eng/smoke` is live in CI (not orphaned); `eng/products.json` agrees with the csproj layout, `prepare-release.yml`'s hardcoded list, and `expectedPackages`; publish-batch's topological sort is correct with a cycle guard; release-plan's tag/version/changelog triple-check fails safe.

| ID | Sev | Cat | Where | Problem | Fix |
|---|---|---|---|---|---|
| INF-1 | **High** | bug | = **X2** — approval printer omits constructors/events | See cross-cutting. | |
| INF-2 | Medium | bug | = **X3** — `{ get; set; init; }` rendering | See cross-cutting. | |
| INF-3 | Medium | bug | `publish-batch.yml:9-11,105-164` | "Re-running is safe" is false for the exact state a failed `release.yml` leaves: release exists, packages absent → product is `pending` again → `gh release create` on the existing tag errors and **aborts the whole batch**. | Check `gh release view` first; if the release exists, skip creation and go straight to the publish wait. |
| INF-4 | Medium | bug | `eng/scripts/update-version.cs:139-160` | Explicit-version bump accepts a silent *downgrade* (e.g. `beta-4` over current `beta-5`) that survives every downstream check into a tag. | Refuse (or warn on) explicit versions that don't sort above current by SemVer precedence. |
| INF-5 | Medium | drift | `Directory.Build.targets:40-42` | The `Kontent.Ai.Delivery.Caching` swap entry can never fire (no project references it, no floor declared) — violating the file's own "in step with products.json" rule. | Delete; re-add with a floor when a real consumer appears. |
| INF-6..20 | Low | — | various | `FindRepoRoot` fails in git worktrees (`.git` is a file) ×5 scripts; changelog promotion uses substring `Contains`/global `Replace` (corruption risk on unusual content); update-version usage string omits `prerelease`/`release` options; dependency-floors "latest" column can report wrong version (unsorted AddRange); floors regex breaks on reordered/multi-line entries (use `XDocument`); prepare-release can leave a pushed branch with no PR if `gh pr create` fails; release.yml interpolates step outputs into `run:` against its own pass-as-env discipline (in the job holding NuGet rights); dangling "(§7)" comment in ci.yml; smoke `library` header overclaims (Caching/Urls restored, never exercised); `NamedClients` whitespace check only catches spaces, not tabs/newlines; most of `FatalExceptions`' list is unreachable on net10.0; smoke/scripts dirs shadow root props but still inherit root **targets** (silent isolation leak — add empty `Directory.Build.targets` beside each); greedy tag-parsing regex splits at last `-v`; two pointless indirections in publish-batch jq + release-status LINQ; csproj indentation (→X6). | Per-item. |

---

# Fix plan

Ordered by severity and deadline pressure. Effort: S < 1h, M = hours, L = a day+ per item. No commits have been made; each phase is sized to be a reviewable PR (or small PR series) per product, matching the repo's "keep PRs scoped" rule.

## Phase 0 — Gate and contract integrity (do first; everything else is judged through these)

| # | Item | Findings | Effort | Why first |
|---|---|---|---|---|
| 0.1 | Fix the approval printer: render constructors + events, fix `init` rendering; re-approve all snapshots in the same PR with a line-by-line review | X2, X3 | M | Every subsequent public-surface change in this plan is validated by this gate; fix the gate before trusting it. |
| 0.2 | Fix `RethrowIfCanceled` timeout misclassification in `src/common`; add regression tests in all three consuming SDKs | X1 / MGT-H1 | M | Cross-product correctness bug on the documented contract; worst on the write API. |
| 0.3 | Sync: dispose `IApiResponse` in `ToSyncResultAsync` (mirror Management) | SYN-H1 | S | Every-request resource leak; two-line fix. |

## Phase 1 — User-visible defects (real inputs produce wrong behavior today)

| # | Item | Findings | Effort |
|---|---|---|---|
| 1.1 | aspnetcore: `ParseNumeric` → invariant `TryParse`, null on failure | ASP-H1 | S |
| 1.2 | generator: identifier-collision family — dedupe on sanitized identifiers, constant-after-property ordering; cross-type filename collision warn-and-skip | GEN-H1, GEN-M6 | M |
| 1.3 | generator: silent-failure holes — default arm in the exception switch, drop/fix the `AggregateException` case, honest "kept existing file" reporting, management-mode environment id | GEN-M5, M7, M8, M9 | M |
| 1.4 | Delivery: eager-refresh race — classify hit/fail-safe from FusionCache state, not captured locals | DEL-H3 | M |
| 1.5 | Delivery: handler-rotation fix (`PooledConnectionLifetime`) | DEL-H2 | S |
| 1.6 | Management: timeout strategy — decide infinite `HttpClient.Timeout` vs documented cap; then M2's loud-fail on non-replayable upload retry | MGT-M1, M2 | M |
| 1.7 | Delivery: `ConfigureAwait(false)` sweep in rich text (+ consider CA2007) | DEL-M3 | S |
| 1.8 | infra: publish-batch resume-after-failed-release; update-version downgrade guard | INF-3, INF-4 | M |

## Phase 2 — Time-boxed breaking-change decisions (windows close; decide now even if work lands later)

These are *decisions* first, code second. Each needs release-notes/upgrade-guide entries and snapshot re-approval per the playbook.

| # | Product | Decision | Findings | Window closes |
|---|---|---|---|---|
| 2.1 | **Sync 2.0** | Fold Abstractions into `Kontent.Ai.Sync` (type-forwarding shim for one release) — or document keeping it as deliberate divergence. Replace the 4× interface/record duplication with sealed records. Type `Data` as `JsonElement?` or better. | SYN-ARCH, H2, H3 | When 2.0 ships |
| 2.2 | **Management GA** | `LastModified`: `DateTime` → `DateTimeOffset` across 8 response models, or explicitly accept and close the question. Delete dead unkeyed API registrations. Doc sweep (L5–L8) before the snapshot freezes. | MGT-L10, M3, L5-L8 | rc.1 |
| 2.3 | **Generator 10.x beta** | Remove the fossil stratum in one coordinated PR: custom-partial path, identity `IDeliveryElementService`, always-true virtuals, both factory pairs, `GeneralGenerator` merge, dead `Property`/`TextHelpers` members. These reference each other — remove together. | GEN-H2, H3, H4, M10-M16 | GA of 10.x |
| 2.4 | **aspnetcore 0.x** | One breaking-cleanup PR: delete dead `Reference`, seal everything, webhook DTOs → sealed records (+`DateTimeOffset`), primary constructors, `[HtmlAttributeNotBound]`, overload disambiguation. | ASP-M2, M4, M5, L9, L10, L18 | 1.0 |
| 2.5 | **Delivery next major** (backlog, no action now) | `Action<object>` → typed overload; `ImageUrlBuilder` silent no-op string overloads → throw. | DEL-M6, L18 | next major |

## Phase 3 — Structural simplification (internal, non-breaking, high leverage)

| # | Item | Findings | Effort |
|---|---|---|---|
| 3.1 | Delivery: extract `CachedQueryExecutor<T>` across the six query builders (do *after* 1.4 so the race fix lands once) | DEL-H1 | L |
| 3.2 | Delivery: replace the component-codename heuristic with structural identification | DEL-M1 | M |
| 3.3 | Delivery: explicit `TagName` instead of `"Tag="` description parsing; hybrid-cache docs-vs-L1 decision; `JsonSerializerOptions` bridging fix; element-failure logging decision | DEL-M5, M2, M7, M8 | M |
| 3.4 | Delivery: `TypeProvider` — drop `GetCallingAssembly` fallback | DEL-M4 | S |
| 3.5 | Sync: dead registration, `ChangeType` converter attribute, auth-handler dead branches, options-block dedup, accessor snapshot fix, `ErrorCode` backfill removal | SYN-M1-M8 | M |
| 3.6 | infra: dead Caching swap entry; workflow hygiene (env-var interpolation in release.yml, prepare-release branch cleanup trap, worktree-safe `FindRepoRoot`, changelog-promotion regex, floors `XDocument` parse, empty `Directory.Build.targets` for smoke/scripts) | INF-5, 6, 7, 10, 11, 12, 17 | M |
| 3.7 | aspnetcore: raw-bytes HMAC (removes M3's dead guard + false comment); rendition-URL helper share | ASP-M3, M6 | M |

## Phase 4 — Comment noise, doc drift, and low-severity polish (mechanical; batch per product)

One cleanup PR per product, low review cost:

- **Cross-product:** X4 wrapper deletions, X5 defensive-guard sweep, X6 csproj indentation.
- **Delivery:** all L-items not covered above (dead code, narration comments, IntelliSense doc fixes, `Examples/` folder, converter exception type, double-`?` URL join, `SerializedFilterCollection` trim).
- **Management:** L1–L9, L11 (docs, Kentico relics, identifier-ctor null checks, `Component.Content` guard, patch-factory validation consistency).
- **Sync:** L1–L12 (GlobalUsings, ctor-order trap, test cleanups, `// Arrange/Act/Assert` drift).
- **Generator:** L17–L34 (invariant casing — small but real, do early in the batch; comment drift; undisposed workspaces; emitted-file unused usings; arg-validation `Enum.IsDefined`).
- **aspnetcore:** L7, L8, L11–L17 (suppress-output on null asset, cancellation token, doc fixes, collection expressions, `typeof` context key).
- **infra:** INF-8, 9, 13–16, 18–20 (usage strings, floors "latest" column, ci.yml §7, smoke header, `NamedClients` whitespace, `FatalExceptions` trim, tag-regex, jq/LINQ indirections).

## Suggested sequencing

```
PR 1  infra: approval printer + snapshot re-approval          (0.1)
PR 2  common: RethrowIfCanceled + tests ×3 SDKs               (0.2)
PR 3  sync: response disposal                                 (0.3)
PR 4  aspnetcore: ParseNumeric                                (1.1)
PR 5  generator: collision + silent-failure fixes             (1.2, 1.3)
PR 6  delivery: race fix + handler rotation + ConfigureAwait  (1.4, 1.5, 1.7)
PR 7  management: timeout strategy + upload replay guard      (1.6)
PR 8  infra: workflow resume + downgrade guard                (1.8)
—— decisions for 2.1–2.4 made here; then their PRs ——
PR 9+ phase 3 structural PRs, one per product
PR N  phase 4 cleanup batches, one per product
```

Phases 0–1 are pure wins with no decisions required. Phase 2 needs your calls — each is flagged with the window it must beat. Phases 3–4 can trail at any pace.
