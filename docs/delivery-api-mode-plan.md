# Giving `DeliveryOptions` an `ApiMode`

Plan for replacing Delivery's four access-mode properties with the `ApiMode` + `ApiKey` pair the
Sync SDK already uses, without breaking anyone.

> [!NOTE]
> **Proposed, not scheduled.** Written up after Sync 2.0 adopted Delivery's *builder* spelling
> (`UseProductionApi(secureAccessApiKey)`, commit `a17da83af`). That change settled the method
> names in Sync's favour of Delivery; this one asks whether the *options* should settle in Sync's
> favour. The two are independent — nothing here is a prerequisite for shipping Delivery 20.

**Verdict: worth doing, but only in a major, and the deprecation must be scoped to the options —
not the builder.** The builder is already mode-shaped; it is the raw options surface that leaks.

---

## 1. What the two SDKs look like today

| | Delivery | Sync |
|---|---|---|
| Options | `UsePreviewApi` (bool), `UseSecureAccess` (bool), `PreviewApiKey`, `SecureAccessApiKey` | `ApiMode` enum, `ApiKey` |
| Builder — public | `UseProductionApi()` | `UseProductionApi()` |
| Builder — secure | `UseProductionApi(secureAccessApiKey)` | `UseProductionApi(secureAccessApiKey)` |
| Builder — preview | `UsePreviewApi(previewApiKey)` | `UsePreviewApi(apiKey)` |
| Key format validated | yes — `[RegularExpression]` on both keys | no |
| Invalid combination | representable, rejected in `Validate` | unrepresentable |

The builders are **already identical** as of `a17da83af`. Only the options differ.

## 2. The actual problem, stated fairly

It is not that Delivery can silently enter an invalid state. It cannot —
`DeliveryOptions.Validate` rejects `UsePreviewApi && UseSecureAccess` with
"Cannot use both Preview API and Secure Access simultaneously," and `[RequiredIf]` guards each
key. The design is *safe*.

The problems are narrower:

1. **Four properties encode three states.** Of the 16 combinations of two booleans and two
   nullable keys, three are meaningful. The rest are rejected at runtime rather than being
   impossible to write.
2. **`appsettings.json` is harder than it needs to be.** A reader must know that preview and
   secure are mutually exclusive, and which of two key properties pairs with which flag:

   ```jsonc
   // Delivery today
   { "UsePreviewApi": true, "PreviewApiKey": "..." }
   // Sync today
   { "ApiMode": "Preview", "ApiKey": "..." }
   ```
3. **Two SDKs, two shapes, one concept.** Anyone configuring both writes different JSON for the
   same intent — the papercut the builder rename just removed at the code level.
4. **`GetApiKey` carries a precedence that validation makes unreachable.** It checks
   `UseSecureAccess` before `UsePreviewApi`; with both true it would return the secure key, but
   both-true never survives validation. Dead ordering, harmless, and it disappears with an enum.

Note that `DeliveryOptions.CopyTo` already delegates to `OptionsCopier<DeliveryOptions>`, so
adding properties does not risk the silent-drop bug that motivated that change. Nothing here is
blocked on it.

## 3. Proposed shape

Add to `DeliveryOptions`:

```csharp
public ApiMode? ApiMode { get; set; }
public string? ApiKey { get; set; }
```

`ApiMode` is **nullable during the deprecation window** — that is the whole trick. A non-nullable
enum defaults to `Public`, which is indistinguishable from "the caller never set it," and the
legacy flags could then never win. `null` means "resolve from the deprecated properties."
It becomes non-nullable with a `Public` default in the release that removes them, converging
exactly on Sync's shape.

`Kontent.Ai.Delivery.Abstractions` needs its own `ApiMode` enum with the same three members as
Sync's. The two stay separate types in separate packages — common may not hold product types, and
neither SDK should reference the other.

### Resolution

`DeliveryOptionsExtensions` becomes the single place the mode is decided:

```csharp
internal static ApiMode ResolveApiMode(this DeliveryOptions options) =>
    options.ApiMode
    ?? (options.UsePreviewApi ? ApiMode.Preview
      : options.UseSecureAccess ? ApiMode.Secure
      : ApiMode.Public);
```

`GetBaseUrl` and `GetApiKey` are rewritten on top of it, with `ApiKey` preferred over the legacy
key of the resolved mode. Everything downstream — the auth handler, the client factory, the
caching key prefix — reads those two extensions and needs no change.

### Builder — deprecate nothing

**Recommendation: leave `IDeliveryOptionsBuilder` exactly as it is.** `UseProductionApi()`,
`UseProductionApi(secureAccessApiKey)` and `UsePreviewApi(previewApiKey)` each already set both
flags to a consistent pair, which is an `ApiMode` in all but name. They become one-liners that
assign `ApiMode` and `ApiKey`, and no caller changes.

This is the one place I would push back on the framing that prompted this document. Obsoleting
the preview/secure *builder* hooks would deprecate the good half of the API, contradict the Sync
rename that just standardised on these exact names, and force churn on every consumer for no
gain. The deprecation belongs on the four options properties, which are what actually diverge.

### Validation

- Keep `[RequiredIf]` and `[RegularExpression]` on the legacy properties, so existing
  configurations validate exactly as they do now.
- Add the equivalents for `ApiKey`: required when the resolved mode is `Preview` or `Secure`,
  and the same key-format regex.
- Add one cross-field rule: if `ApiMode` is set *and* either legacy flag is true, and the two
  disagree, fail with a message naming both. Silent precedence in a security-adjacent setting is
  worse than a startup error.
- The existing both-flags-true rule stays while the flags do.

## 4. Migration

| Release | Change |
|---|---|
| Delivery 21 | Add `ApiMode?`/`ApiKey`; mark the four legacy properties `[Obsolete]` naming 22; update README, changelog, samples |
| Delivery 22 | Remove the four properties; make `ApiMode` non-nullable with `Public` default |

Same two-step Sync used for `UseSecureApi` (`a17da83af`). It relies on the removal actually being
scheduled somewhere other than an attribute string — see §6.

Existing `appsettings.json` files keep working untouched through 21 and produce an obsolete-usage
warning only in code that sets the properties directly. Binding does not warn, so config-driven
consumers see nothing until the release notes tell them.

## 5. Cost

Small and concentrated in tests and docs. Two properties, one enum, one resolver, three validation
rules, three builder bodies collapsing to assignments. The public API snapshot moves by the two
new members — and note that it will **not** record the `[Obsolete]` markers, because
`src/testing/PublicApiApproval.cs` does not render attributes. That gate hole was found while
shipping the Sync rename and belongs on the printer-fidelity list.

Cross-product impact is nil, verified rather than assumed: no file under `src/aspnetcore` or
`src/model-generator` names any of the four properties, tests included. The generator does hold a
`DeliveryOptions` (`CodeGeneratorOptions.DeliveryOptions`) and maps CLI arguments onto it, but
only `EnvironmentId`. It binds the rest from configuration, which keeps working unchanged — the
obsolete warnings are compile-time and binding is not compilation.

## 6. Open questions

1. **Is it worth a major on its own?** Almost certainly not. This should ride along with the next
   Delivery major that is happening anyway.
2. **Who tracks the removal?** The Sync `UseSecureApi` deprecation has the same problem: an
   `[Obsolete]` message naming a version is documentation, not a task. If there is no 21/22
   milestone to hang these on, both deprecations become permanent and the unification never
   completes — in which case doing nothing is more honest than deprecating.
3. **Should Sync gain Delivery's key-format regex?** Delivery validates that an API key looks like
   a three-segment token; Sync accepts any non-empty string. That is parity in the other
   direction and is independent of this plan.
4. **`ApiMode` naming.** Sync's members are `Public`/`Preview`/`Secure`. Delivery's vocabulary is
   "Production", "Preview", "secure access". `Public` reads oddly next to `ProductionEndpoint`,
   but divergent member names would defeat the point.
