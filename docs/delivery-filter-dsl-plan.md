# Making `IDeliveryApi` source-generatable

Plan for removing the `Refit.Reflection` dependency from `Kontent.Ai.Delivery` by reshaping how
the filter DSL reaches the wire.

**Verdict: viable, no public API change, smaller than first estimated.** My earlier assessment
(`breaking-changes-net10.md` §5) called this "wire-level, not mechanical" and recommended
deferring. That was based on the assumption that reproducing Refit's query encoding was subtle.
It is not — see §2. The recommendation below is revised accordingly.

---

## 1. The problem, restated

Seven `IDeliveryApi` methods carry the filter DSL as `[Query] Dictionary<string, string[]>`.
Refit 14's source generator cannot build those inline, because the dictionary's keys
(`elements.title[eq]`) exist only at runtime. Those methods fall back to the reflection request
builder, which is why `Kontent.Ai.Delivery` references `Refit.Reflection` and Management and Sync
do not.

Consequences today: a `RF006` suppression in `src/delivery/.editorconfig`, an extra package
dependency, and no path to NativeAOT or trimming.

---

## 2. Feasibility — three things verified, not assumed

### 2.1 The public boundary is below the change

| | |
|---|---|
| **public** | `IItemsFilterBuilder`, `IItemFieldFilter<T>`, `ITypesFilterBuilder`, `ITypeFieldFilter<T>`, `ITaxonomiesFilterBuilder`, `ITaxonomyFieldFilter<T>`, `LanguageFallbackMode` — all in `Delivery.Abstractions`, all pure fluent surface |
| **internal** | all 12 implementation files in `Delivery/Api/Filtering/`, plus `IDeliveryApi` itself |

Nothing a consumer can name changes. `.Element("title").IsEqualTo("x")` is untouched. **No
breaking change, no approval-snapshot update.**

### 2.2 The encoding contract is one expression

Captured Refit's actual output for ten awkward inputs, then reproduced all of them:

```csharp
string.Join("&", pairs.Select(p =>
    $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"))
```

**9/9 reproduced exactly**, including every case previously flagged as risky:

| input | wire |
|---|---|
| `Hello & World` | `Hello%20%26%20World` |
| `Hello%20%26%20World` (pre-encoded) | `Hello%2520%2526%2520World` |
| `Přílič žluťoučký` | `P%C5%99%C3%ADli%C4%8D%20…` |
| `a,b` | `a%2Cb` |
| `a+b/c` | `a%2Bb%2Fc` |
| `""` (empty) | `elements.title%5Bempty%5D=` |
| two values, one key | `k=x&k=y` |

The "deliberate double-encoding guardrail" is simply what `EscapeDataString` does to `%` — not a
bespoke rule to reimplement.

Also discovered: **`CamelCaseUrlParameterKeyFormatter` does not touch dictionary keys.**
`elements.Title[eq]` stays capitalised. One less behaviour to reproduce.

### 2.3 A generator-friendly parameter shape exists

Compiled four candidate signatures against the real Refit 14 generator:

| shape | `RF006` |
|---|---|
| `[Query] Dictionary<string, string[]>` (today) | ❌ |
| `[Property("filters")] string` | ✅ **generates inline** |
| `[Query] IEnumerable<KeyValuePair<string, string>>` | ❌ |
| typed `[Query]` POCO only (baseline) | ✅ |

`[Property]` attaches a value to `HttpRequestMessage.Options` without putting it on the wire —
exactly the escape hatch needed.

---

## 3. Proposed design

```
ItemsFilterBuilder                 unchanged  (public DSL)
  └─ SerializedFilterCollection    unchanged  (ordered List<KeyValuePair<string,string>>)
       └─ FilterQueryString.Render(...)          NEW  — the one expression from §2.2
            └─ IDeliveryApi [Property("filters")] string?   changed from [Query] Dictionary
                 └─ FilterQueryHandler : DelegatingHandler  NEW — appends to RequestUri
```

Deleted: `FilterQueryParams`, `SerializedFilterCollection.ToQueryDictionary`, the
`Refit.Reflection` package reference, and the `RF006` block in `src/delivery/.editorconfig`.

---

## 4. Pros

- **Drops a package dependency** from every consumer of `Kontent.Ai.Delivery`.
- **Removes a pointless allocation round-trip.** Today: ordered pairs → `GroupBy` →
  `Dictionary<string, string[]>` → Refit flattens back to repeated keys → string. The dictionary
  exists *only* because Refit needed one. New path: pairs → string.
- **Fixes an ordering quirk** (see §6.1).
- **Clears the last 7 build warnings** and the suppression that hides them, so `RF006` becomes
  fully live again — a new un-generatable interface would report immediately.
- **Unblocks the AOT conversation** for Delivery. Does not complete it (see §5.4).
- **No public API change**, so it can ship in any release, not only a major.

## 5. Cons and risks

### 5.1 We own the encoding forever

Currently Refit's problem, afterwards ours. Mitigated by it being one verified expression, but
it is a permanent maintenance surface. If Kontent.ai ever changes how filter values are escaped,
the fix is now in our code — which is arguably better, but it *is* a transfer of responsibility.

### 5.2 Retry idempotency — the sharpest risk

The resilience handler sits outermost, so inner handlers re-run on **every retry attempt**. A
handler that appends to `request.RequestUri` unconditionally produces `?a=1&a=1` on attempt two —
silently wrong results, no error.

The handler must be idempotent: rebuild the URI from a stored base rather than appending to
whatever is currently there, and clear or ignore the option once applied. **This needs an explicit
test that asserts a retried request has the same query as the first attempt.**

### 5.3 Out-of-band data flow

Filters travel via `HttpRequestMessage.Options` rather than as a visible parameter, so reading
`IDeliveryApi` no longer tells you the full request shape. Worth an XML-doc note on the parameter
pointing at the handler.

Checked and *not* a problem: `Delivery.Caching` does not key off `RequestUri`, so late URI
mutation cannot cause cache collisions.

### 5.4 It does not achieve AOT on its own

Delivery still has `Activator.CreateInstance` in the content-item converters and reflection-driven
typed-model mapping. This removes one blocker of several. Do not sell it as "AOT-ready".

### 5.5 Cost is concentrated in tests, not code

The production change is perhaps 60 lines net negative. The test work is larger: the current
`FilterRefitSerializationTests` pins four cases; a reimplementation deserves the full matrix from
§2.2 plus the retry test from §5.2.

---

## 6. Other findings from analysing the DSL

### 6.1 The current grouping reorders filters _(latent, harmless today)_

`SerializedFilterCollection.ToQueryDictionary` does `GroupBy(key).ToDictionary(...)`. Given
`a=1`, `b=2`, `a=3` in that order, the wire receives `a=1&a=3&b=2` — same-key values are pulled
together and the original interleaving is lost. The collection is documented as preserving
"duplicates and insertion order", and it does; the *dictionary conversion* is what loses it.

Rendering straight from the ordered list emits `a=1&b=2&a=3`, which is faithful. No known
API-visible impact, but the new design fixes it for free and it should be stated in the changelog
rather than discovered later.

### 6.2 `FilterValueSerializer.Serialize(string)` becomes misleading

```csharp
internal static string Serialize(string value)
{
    ArgumentNullException.ThrowIfNull(value);
    // Encoding is handled by Refit.        ← no longer true after this change
    return value;
}
```

The method is a null-check plus a comment asserting an ownership boundary that moves. Either
update the comment to point at `FilterQueryString`, or fold the null-check into the caller and
delete it.

### 6.3 `params string[]` → `params ReadOnlySpan<string>` _(separate decision)_

31 public signatures across the filter DSL and Management patch factories —
`IsIn(params string[])`, `ContainsAll(string[])`, `ReplaceAllowedRoles(params Reference[])`. Saves
an array allocation per call and the filter DSL is called per query. **Binary-breaking but
source-compatible**, so the current major-version window is the only cheap opportunity. Unrelated
to this refactor; listed here because it touches the same files.

### 6.4 The DSL shape itself is sound

`IItemFieldFilter<TBuilder>` uses a self-referencing generic for fluent chaining across items,
types and taxonomies without duplicating the operator surface. Typed overloads (`IsEqualTo(string)`
/ `(double)` / `(DateTime)` / `(bool)`) push value formatting into `FilterValueSerializer` with
invariant culture and explicit UTC normalisation. No change recommended.

---

## 7. Recommendation

**Do it, but not on this branch.**

It no longer needs the major-version window — §2.1 establishes there is no public API change, so
it can ship in any minor. That removes the only argument for rushing it alongside the .NET 10
release, which is already carrying five majors, a Refit upgrade, an enum migration and a public
API removal.

Sequence when picked up:

1. Write the characterisation tests **first**, against the current reflection implementation, using
   the §2.2 matrix. They must pass unchanged afterwards — that is the whole safety argument.
2. Add `FilterQueryString.Render` and unit-test it against the same matrix.
3. Switch `IDeliveryApi` to `[Property]`, add the handler, delete `FilterQueryParams` and
   `ToQueryDictionary`.
4. Add the retry-idempotency test (§5.2).
5. Drop `Refit.Reflection` and the `RF006` suppression; confirm the build reports zero warnings
   with `RF006` live.
6. Changelog the ordering fix (§6.1).

Estimated: half a day of production code, a day of tests.

Until then the `Refit.Reflection` reference is correct and documented, and — per
`breaking-changes-net10.md` §5 — carries no version ceiling.
