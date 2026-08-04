# Breaking changes — `feat/net10`

Working document for the coordinated .NET 10 release. Every product takes a major on this
branch, so this is the record of what breaks and why, and the source material for the final
per-product `CHANGELOG.md` entries.

**Status:** in progress. Sections marked _(planned)_ are decided but not implemented.

---

## Why every product needs a major

All five products moved from a single `net8.0` target to a single `net10.0` target — no
multi-targeting at any point. A consumer on net8 therefore cannot install the next release at
all:

```
error NU1202: Package Kontent.Ai.Delivery is not compatible with net8.0
              (.NETCoreApp,Version=v8.0). Package supports: net10.0
```

That is breaking regardless of source compatibility, and it applies to every package in the
repo. `Kontent.Ai.Delivery.SourceGeneration` is the one exception — it stays `netstandard2.0`
because Roslyn components must, and it is unaffected.

### Version implications

| Product | Published | Next | Note |
|---|---|---|---|
| Delivery | 19.4.0 | **20.0.0** | `Delivery`, `.Abstractions`, `.Caching`, `.SourceGeneration`, `Urls` ship lockstep |
| Sync | 1.0.0 | **2.0.0** | |
| Model generator | 10.3.0-beta-2 | **11.0.0** | 10.3.0 is a minor line; `.Core` is a consumable library |
| Management | 9.0.0-beta-5 | **9.0.0-rc.1** | already mid-major, absorbs the break; `rc.N` avoids the `beta-10` sort cliff |
| ASP.NET Core | 0.17.0-preview.1 | **0.18.0-preview.1** | pre-1.0, minor carries breaking changes |

Because every product is taking a major anyway, this branch is the cheapest opportunity to
land deferred breaking changes. Anything breaking that has been waiting should go in now.

---

## 1. Target framework — all products

**Breaking.** `net8.0` → `net10.0`. Consumers must move to .NET 10.

---

## 2. Refit 10.2.0 → 14.0.1 — Delivery, Management, Sync

Four majors at once. The compile-level fallout was small; the architectural change is not.

### 2.1 Reflection is now opt-in — Delivery only _(consumer-visible dependency)_

Refit 14 source-generates request logic at compile time and treats the reflection request
builder as an opt-in package. Interfaces that cannot be fully generated throw at runtime:

```
System.NotSupportedException: This interface needs the reflection request builder,
which is not installed.
```

`IManagementApi`, `ISubscriptionApi` and `ISyncApi` generate completely and are unaffected —
they silently gained generated request building. `IDeliveryApi` does not: seven methods raise
`RF006`, all of them the ones carrying the filter DSL. See §5 for the detail.

**Consequence:** `Kontent.Ai.Delivery` now has a new package dependency, `Refit.Reflection`.
This restores the pre-14 behaviour exactly — Refit ≤13 used reflection for everything — but it
is a new entry in the dependency graph and should be called out.

### 2.2 `IApiResponse.StatusCode` is now nullable — internal only

Refit added `IsReceived`; `StatusCode` is null when no response was ever received (a
transport-level failure). All three products funnel responses through an internal
`RefitApiResponseExtensions`, which now resolves the status as:

```
response.StatusCode ?? (response.Error as ApiException)?.StatusCode ?? default
```

The final fallback is `default` rather than an invented code, because no HTTP exchange
completed and any real status would misreport what happened.

**Observable effect:** a request that fails at the transport layer before any response
arrives now surfaces `(HttpStatusCode)0` on the result object instead of throwing inside the
mapper. No test changed, but it is a behaviour worth a changelog line.

### 2.3 `IApiResponse.Error` widened to `ApiExceptionBase` — internal only

Only `ApiException` buffers the response body. Management's error-parsing path now
pattern-matches and falls back to a bare message for the other kinds. No public surface change.

### 2.4 `Refit.UrlAttribute` — internal only

Refit gained a `UrlAttribute`, which collides with
`System.ComponentModel.DataAnnotations.UrlAttribute` in Management, where `GlobalUsings.cs`
has a `global using Refit;`. Resolved by fully qualifying the one usage in
`ManagementOptions.Endpoint`.

### 2.5 Refit removed from the public API

**Breaking, all three products.** Refit no longer appears anywhere in the approved public
surface. Removed:

| Product | Removed |
|---|---|
| Delivery | `configureRefit` parameter from 6 × `AddDeliveryClient` |
| Delivery | `public sealed class RefitSettingsProvider` — now `internal` (both members) |
| Management | `configureRefit` parameter from 5 × `AddManagementClient` |
| Management | `ManagementClientBuilder.ConfigureRefit(Action<RefitSettings>)` |
| Sync | `configureRefit` parameter from 3 × `AddSyncClient` |

**Expected impact: near zero.** Everything reachable through the hook was load-bearing, so
there was nothing a caller could safely change through it:

- `CollectionFormat.Multi` is what keeps duplicate filters as separate query parameters.
  `Csv` would collapse `[eq]=a&[eq]=b` into a single `a,b` the API reads as one string —
  wrong results, no error.
- `CamelCaseUrlParameterKeyFormatter` is what turns POCO property names into the casing the
  API expects.
- The `JsonSerializerOptions` carry `MaxDepth = 124` (the API's nesting limit), the naming
  policy, and the three converters for the wire format.

Corroborating that: no guide documented the hook (`extensibility-guide.md` offers Type
Providers, property-mapping conventions and rich-text resolvers, none of which route through
it), and the SDK's own tests only ever passed `null` or an empty lambda asserting the hook
fired — not one changed a setting.

It is still a public-signature removal, so it belongs in the changelog and the upgrade guide.
If a genuine use case surfaces, it should come back as a targeted API named after what it
does rather than after the transport library.

Approval snapshots updated in all three products; diffs reviewed line by line and contain
only these removals.

---

## 3. Dependency floors raised — consumer-visible

Consumers restoring these packages resolve the new minimums. Not breaking in the compile
sense, but it forces transitive upgrades and belongs in the changelog.

| Product | Package | From | To |
|---|---|---|---|
| Delivery | `Microsoft.Extensions.*` band | 9.0.15 | 10.0.10 |
| Delivery | `Microsoft.Extensions.Http.Resilience` | 9.6.0 | 10.8.0 |
| Delivery | `AngleSharp` | 1.5.0 | 1.7.0 |
| Delivery (`Urls`) | `Microsoft.Extensions.Primitives` | 9.0.15 | 10.0.10 |
| Delivery (`.Caching`) | `ZiggyCreatures.FusionCache` (+ STJ serializer) | 2.5.0 | 2.6.0 |
| Delivery (`.Caching`) | `Microsoft.Extensions.Caching.*` | 8.0.0 / 10.0.6 | 10.0.10 |
| Delivery | `Refit`, `Refit.HttpClientFactory` | 10.2.0 | 14.0.1 |
| Delivery | `Refit.Reflection` | — | 14.0.1 *(new)* |
| Management | `Microsoft.Extensions.*` band | 9.0.15 | 10.0.10 |
| Management | `Microsoft.Extensions.Http.Resilience` | 9.6.0 | 10.8.0 |
| Management | `Refit`, `Refit.HttpClientFactory` | 10.2.0 | 14.0.1 |
| Sync | `Microsoft.Extensions.*` band | 9.0.15 | 10.0.10 |
| Sync | `Microsoft.Extensions.Http.Resilience` | 9.6.0 | 10.8.0 |
| Sync | `Refit`, `Refit.HttpClientFactory` | 10.2.0 | 14.0.1 |
| Model generator | `Microsoft.CodeAnalysis` | 4.13.0 | 5.6.0 |
| Model generator | `Microsoft.Extensions.*` | 9.0.15 | 10.0.10 |

ASP.NET Core has no direct `Microsoft.Extensions.*` references and needs no floor entry.

Test-only bumps (`NodaTime`, `StackExchange.Redis`, `AwesomeAssertions`, `xunit.runner.*`,
`Microsoft.NET.Test.Sdk`, `coverlet`, `BenchmarkDotNet`) and `PrivateAssets=all` analyzer
bumps (`SonarAnalyzer.CSharp`, `Microsoft.SourceLink.GitHub`) are **not** consumer-visible and
should stay out of the changelogs.

---

## 4. `Kontent.Ai.Delivery.SourceGeneration` — no change to its compiler floor

Deliberately excluded from the repo-wide Roslyn bump. The version compiled against sets the
oldest compiler that can *load* the generator; a newer one is skipped with `CS9057` and emits
nothing at all. It stays on `Microsoft.CodeAnalysis.CSharp` 4.9.2 via `VersionOverride`, so the
emitted assembly still references `4.9.0.0` and loads in every SDK from .NET 8.0.2xx onward.

Nothing changes for consumers. Recorded here so the pin is not "tidied up" later.

---

## 5. Why `IDeliveryApi` cannot be source-generated

Seven methods raise `RF006`. The correlation with the filter DSL is exact:

| File | Methods with `[Query] Dictionary<string, string[]>` | `RF006` |
|---|---|---|
| `IDeliveryApi.Items.cs` | 3 | 3 |
| `IDeliveryApi.UsedIn.cs` | 2 | 2 |
| `IDeliveryApi.Types.cs` | 1 | 1 |
| `IDeliveryApi.TaxonomyGroups.cs` | 1 | 1 |
| `IDeliveryApi.Languages.cs` | 0 | 0 |

Every flagged method carries the filter dictionary; every method without it generates cleanly,
including generic ones and ones with typed `[Query]` POCOs:

```csharp
[Query] ListTypesParams? queryParameters = null       // generates — property names known at compile time
[Query] Dictionary<string, string[]>? filters = null  // does not — keys are runtime values
```

The generator emits straight-line query-building code. For a typed POCO it knows the property
names while compiling. For the filter dictionary it cannot: keys like `elements.title[eq]` only
exist at runtime, and rendering them additionally depends on two runtime `RefitSettings`
values — `CollectionFormat.Multi` (repeat the key once per value, which is how duplicate
filters stay separate query parameters) and `CamelCaseUrlParameterKeyFormatter`.

### What a fix would require

Making it generator-friendly means taking ownership of query-string construction — building
the query ourselves and passing it through as a pre-rendered, unescaped segment. That is a
wire-level change, not a mechanical one, because it must reproduce the current encoding
semantics exactly. `FilterRefitSerializationTests` pins four of them:

- a raw value is encoded exactly once — `Hello & World` → `Hello%20%26%20World`
- a **pre-encoded** value is encoded *again* — deliberate guardrail, not a bug
- operator suffixes are encoded in the key — `elements.title[empty]` → `elements.title%5Bempty%5D`
- duplicate keys become repeated query parameters, in insertion order

### Recommendation: not now

The only gains are dropping the `Refit.Reflection` dependency and unblocking NativeAOT and
trimming for Delivery. Nothing in the repo currently claims AOT or trim support — there is no
`IsAotCompatible`, `IsTrimmable` or `PublishAot` anywhere — so nothing regresses by waiting.
Against that, the risk is silently changing the URLs sent to the Delivery API.

**This does not pin us to a Refit version.** `Refit.Reflection` is not a legacy package left
behind by an old release — it is first-party (`reactiveui/refit`, same commit as Refit itself)
and was introduced *with* Refit 14, whose generator-first change created the need for it. Its
published versions track Refit's exactly:

```
Refit             14.0.0-beta.1 … 14.0.0, 14.0.1, 15.0.0-beta.1
Refit.Reflection  14.0.0-beta.1 … 14.0.0, 14.0.1, 15.0.0-beta.1
```

No version carries deprecation metadata, and the 15 beta still ships it. Refit ≤13 had the
reflection builder built in; 14 extracted it as an opt-in.

The residual risk is a future retirement — the package exists only to serve shapes the
generator cannot build, and its own description points users toward generated clients. The
signal to watch is concrete: **a Refit release that ships without a matching
`Refit.Reflection`.** That is the point to do this work, along with AOT becoming a real
requirement. Either way, do it with a broader encoding test matrix than the four cases above.

---

## 6. Enum serialization — Management

The custom `EnumMemberJsonConverter` is gone. Enums now carry
`[JsonStringEnumMemberName("...")]` instead of `[EnumMember(Value = "...")]` and serialize through the
built-in `JsonStringEnumConverter`, which honours that attribute from .NET 9 onward. The converter only
existed because it did not on net8.

**No wire change.** All 140 members across 36 enums keep their exact tokens, verified by round-trip. The
attribute is a verbatim literal, so the mixed shapes the API actually uses survive untouched — snake_case
(`modular_content`), kebab-case (`light-purple`, `heading-one`), camelCase (`fullScreen`), abbreviations
(`asc`), and outright renames (`LinkedItems` → `modular_content`). Naming policies were considered and
rejected: `SnakeCaseLower` reproduces only 117 of 140, and per-member literals match the convention already
used for properties.

**Behaviour change: reads are now case-sensitive.** _(breaking, near-zero impact)_

| input | before | after |
|---|---|---|
| `"modular_content"` | `LinkedItems` | `LinkedItems` |
| `"MODULAR_CONTENT"` | `LinkedItems` | throws `JsonException` |
| `"LinkedItems"` (C# member name) | `LinkedItems` | throws `JsonException` |

The old converter matched tokens case-insensitively and fell back to member-name parsing. Nothing depended
on it: making the old converter strict failed exactly one test — `Read_FromEnumMemberValue_CaseInsensitive`,
which asserted the leniency directly — and left the other 916 passing. The API emits canonical tokens,
`ContentModelSnapshot.FromJson` is contractually only fed `ToJson` output, and writes never parse. Now pinned
in the other direction by `EnumSerializationTests.Read_IsCaseSensitive`.

Unchanged: numeric tokens are still rejected (`allowIntegerValues: false`), undefined values still throw on
write, and dictionary keys still round-trip.

`EnumWire` survives — PATCH path segments and polymorphic discriminators need a wire token with no serializer
in play. It now reads the new attribute and is a separate implementation rather than a shared map, so
`EnumWire_AgreesWithTheSerializer_ForEveryMember` pins that the two cannot drift.

---

## 7. Still to decide or implement

- **§2.5 Refit abstraction** — decided, not implemented.
- **S2365** — `ClassCodeGenerator.Properties` and
  `DeliveryClassCodeGeneratorBase.PropertyCodenameConstants` are `protected` properties that
  copy collections on each access; converting them to methods is source-breaking for
  subclassers. Currently suppressed in `src/model-generator/.editorconfig` with that reasoning.
