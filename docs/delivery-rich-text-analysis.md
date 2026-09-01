# Delivery's rich text subsystem

Analysis of `ContentItems/RichText`, `ContentItems/Processing/RichTextParser.cs`,
`ContentItems/Elements/RichTextElementEnvelopeReader.cs` and `Extensions/RichTextExtensions.cs` —
the area the query-builder trace listed as never examined.

> [!NOTE]
> **Implemented 2026-09-01**, in the order §6 sets out, as seven commits on `vnext`:
> `read one rich text envelope the same way on both paths` (§2.1) ·
> `let ThrowOnMissingResolver compose in a fluent chain` (§2.3) ·
> `encode rich text with one encoder throughout` (§2.4) ·
> `give the block resolvers their own typed fields` (§3.1, and §5's null-or-empty branching) ·
> `cache the embedded content model type lookup` (§5) ·
> `drop the unread container snapshot and the decorative generic` (§4) ·
> `point resolver registration at the typed overload` (§2.2, §3.2).
> Whole monorepo green in both reference modes; the only approval-snapshot change is §2.3's two lines.
> §8 stands: `Kontent.Ai.Delivery.Caching` internals remain untraced.
>
> **The analysis below is preserved as written, corrections included.**
> Every behavioural claim below was verified against the code, and the three that could not be settled
> by reading were verified by running a throwaway probe (since deleted). Where a probe result
> contradicted the initial reading, the finding records the probe.
>
> **Revised 2026-09-01 after a second cross-check against the code and against the shipped surface.**
> Five claims were corrected; each correction is marked **Correction** in place. The headline change:
> §2.2 and §3.2 are no longer proposed as breaking changes, which makes the whole plan non-breaking.
> Baseline at the time of the recheck: `dotnet build` clean, 1,145 Delivery tests green.

**Verdict: the parser and the block model are sound. The weight and all of the risk are in the
resolution layer — 906 of the subsystem's 1,924 production lines, of which 562 are a builder
interface and its implementation populating six collections. Three findings are behavioural and
worth fixing regardless of any refactor.**

---

## 1. Shape

| Area | Lines | State |
|---|---|---|
| Resolution (`IHtmlResolverBuilder`, `HtmlResolverBuilder`, `HtmlResolver`, `DefaultResolvers`, `ConditionalHtmlNodeResolver`) | 906 | where everything below lives |
| `RichTextExtensions` | 294 | 9 public extension methods, 5 of them one-line wrappers over `GetBlocks<T>` |
| `RichTextParser` | 228 | clean recursive descent, depth-guarded at 100 (API max 124) |
| `RichTextElementEnvelopeReader` | 122 | two callers that disagree — see §2.1 |
| `RichTextContent`, `RichTextElementData`, blocks, interfaces | 374 | minimal records; fine |

## 2. Behavioural findings

### 2.1 The typed and dynamic paths read the same envelope differently

`RichTextElementEnvelopeReader.Read` has exactly two production callers, and they disagree on **two
of its three optional parameters**:

| Caller | `serializerOptions` | `preserveEmptyModularContentEntries` |
|---|---|---|
| `ElementValueMapper.cs:109` (typed models) | *not passed* → `null` → case-**sensitive** | `true` |
| `RichTextExtensions.cs:231` (dynamic `ParseRichTextAsync`) | `PropertyNameCaseInsensitive = true` | `false` |

`ElementValueMapper` **holds** the SDK's shared `JsonSerializerOptions` — it is a constructor
parameter, used at line 69 — and simply does not pass it here. So on the typed path, `InlineImage`
and `ContentLink` are deserialized with `JsonSerializerOptions.Default`: no shared converters, and
case-sensitive matching against `[JsonPropertyName("image_id")]`.

`InlineImage.Url` is `required`, so a recased property from the API would **throw** on the typed path
and **succeed** on the dynamic one. This is the same mistake the Sync SDK made with
`PropertyNameCaseInsensitive` — the reasoning that explicit attributes settle everything — except
here the two halves of one SDK disagree with each other.

The `preserveEmpty` divergence is separate and unexplained: the same `modular_content` array keeps
empty entries on one path and drops them on the other, with no comment saying why either is right.

> **Correction.** The `preserveEmpty` half cannot corrupt anything today. `ModularContent` has exactly
> two consumers downstream of the reader: `ContentDependencyExtractor.ExtractFromRichTextElement`,
> whose `DependencyTrackingContext.TrackItem` already returns early on null-or-whitespace, and
> `RichTextContent.ModularContentCodenames`, which §4 shows is dead. The divergence is real and
> unexplained but inert. Only the `serializerOptions` half can throw, so "the only finding that can
> corrupt data" applies to that half alone.
>
> The blast radius of passing the shared options was checked and is safe: all three converters on the
> shared instance are type-scoped (`ContentItemConverterFactory` → `ContentItem<>`,
> `ContentElementConverter` → `ContentElement`, `ContentElementDictionaryConverter` →
> `IReadOnlyDictionary<string, ContentElement>`) and none of them match `InlineImage`, `ContentLink`
> or `List<string>`; `PropertyNamingPolicy = CamelCase` is inert because every property on those
> records carries an explicit `[JsonPropertyName]`. Case-sensitivity is the only behaviour that moves.

### 2.2 `GetEmbeddedContent<T>` and `GetEmbeddedContentOfType<T>` differ silently in depth

```csharp
GetEmbeddedContent<TModel>(this IRichTextContent)                  => GetBlocks<…>()  // recursive
GetEmbeddedContentOfType<TModel>(this IEnumerable<IRichTextBlock>)  => OfType<…>()    // top level only
```

`IRichTextContent : IReadOnlyList<IRichTextBlock>`, so **both are callable on the same object**, and
the XML example on `GetEmbeddedContentOfType` shows exactly that (`richText.GetEmbeddedContentOfType<Coffee>()`).

Verified by probe. Rich text with an `<object>` nested inside a table cell — legal in Kontent.ai
rich text, so not a contrived case — parsed through `ParseRichTextAsync`:

```
topLevelBlocks=2  GetEmbeddedContent<T>(recursive)=1  GetEmbeddedContentOfType<T>(flat)=0
```

Two methods one word apart, called on the same value, one finds the embedded item and one silently
returns nothing. Neither doc comment mentions depth.

> **Correction.** The first draft treated `GetEmbeddedContentOfType<T>` as redundant and floated
> removing it. It is not redundant. It extends `IEnumerable<IRichTextBlock>`, a receiver
> `GetEmbeddedContent<T>` cannot reach — there is no way to obtain an `IRichTextContent` from a
> block's `Children`, so filtering an arbitrary block sequence has no other route:
>
> ```csharp
> someBlock.Children.GetEmbeddedContentOfType<Article>()
> richText.GetBlocks<IHtmlNode>().SelectMany(n => n.Children).GetEmbeddedContentOfType<Article>()
> ```
>
> The defect is narrower than stated: the *documentation* teaches the one call that is ambiguous. Fix
> the XML example to take a block sequence as its receiver, and state depth explicitly on both methods
> ("searches nested blocks" / "filters the given sequence only"). The container-receiver call still
> compiles and cannot be prevented, but it stops being the documented usage. **No public API change.**
>
> Note also that "no callers in this repo" — true; no test, no README, no docs page reference it — is
> evidence about our own dogfooding, not about external users. See the shipped-surface note in §3.2.

### 2.3 `ThrowOnMissingResolver` cannot be called in a fluent chain

It is declared only on the concrete `HtmlResolverBuilder`, returning `HtmlResolverBuilder`. Every
`With*` method returns `IHtmlResolverBuilder`, where it does not exist. Verified by compiling:

```csharp
new HtmlResolverBuilder()
    .WithTextNodeResolver(…)
    .ThrowOnMissingResolver()   // error CS1061: 'IHtmlResolverBuilder' does not contain
    .Build();                   // a definition for 'ThrowOnMissingResolver'
```

It works only as the *first* call after `new`. The single call site in the tests
(`RichTextIntegrationTests.cs:1030`) does exactly that — the only position that compiles.

### 2.4 The text encoder does not do what its comment says

```csharp
// Create HTML encoder that preserves Unicode characters (emojis, smart quotes, accented chars)
var unicodeEncoder = HtmlEncoder.Create(UnicodeRanges.All);
```

`UnicodeRanges.All` is the Basic Multilingual Plane only (U+0000–U+FFFF). Emoji live in a
supplementary plane, so they are **still escaped**. Verified by probe — the same source characters in
a text node and in an image description:

```
<p>café &#x1F600;</p>
<figure><img … alt="caf&#xE9; &#x1F600;" …/></figure>
```

So `é` survives in text but not in an attribute (attributes and `alt` use `HtmlEncoder.Default`), and
the emoji survives in neither, despite the comment naming emojis first. The output is valid HTML
either way; the defect is that the comment states a guarantee the code does not provide, and that one
document is emitted with two encoders.

## 3. Type safety and over-generalisation

### 3.1 `Dictionary<Type, Delegate>` with casts back out

`HtmlResolver` receives `IReadOnlyDictionary<Type, Delegate>` and casts on use:

```csharp
((BlockResolver<ITextNode>)resolver)(textNode, …)
((BlockResolver<IInlineImage>)resolver)(image, …)
```

The builder writes exactly four keys (`IContentItemLink`, `IInlineImage`, `ITextNode`, `IHtmlNode`),
and `Build()` filters `IHtmlNode` out before construction, so **three** keys ever reach the
dictionary and all three are read at fixed, statically known types. This is an untyped dispatch table
standing in for three fields — the same shape as the `object`+`Type` deserializer contract that was
removed in `65dfd4b3b`, and fixable the same way:

```csharp
BlockResolver<ITextNode>?        TextNodeResolver
BlockResolver<IInlineImage>?     InlineImageResolver
BlockResolver<IContentItemLink>? ContentItemLinkResolver
```

### 3.2 Four `WithContentResolvers` overloads reduce to two bodies

The dictionary-keyed and `params`-tuple-keyed variants are **byte-identical in each pair** (verified
by diffing the method bodies). Worse, the `Type`-keyed pair is strictly inferior to the generic
single registration and **bypasses the type guard the generic version applies**
(`content is IEmbeddedContent<TModel> ? … : empty`).

The SDK's own test shows the cost:

```csharp
.WithContentResolvers(
    (typeof(Tweet), content => content is IEmbeddedContent<Tweet> tweet ? $"…" : string.Empty),
```

The type is named twice — once as the dictionary key, once as a cast — and the caller is forced to
write an `else` branch that cannot be reached. The alternative already exists and states it once:

```csharp
.WithContentResolver<Tweet>(tweet => $"…")
```

> **Correction — three separate errors above.**
>
> **(a) The surface is GA-published, not pre-GA.** `git show delivery-v19.4.0:…/PublicApiApprovalTests…verified.txt`
> lists `GetEmbeddedContentOfType` and all four `WithContentResolvers` overloads. They entered in
> `ee18cec3d` (2025-10-26), shipped from `19.0.0-beta-3` onward, and have been in every stable 19.x
> since 19.0.0 GA (2026-04-19); 19.4.0 is the current stable on nuget.org. Removing either is a
> removal from a GA surface taken in a major — permissible, but it needs a changelog breaking-change
> entry with migration guidance, not a pre-GA hand-wave.
>
> **(b) The `Type`-keyed pair is not "strictly inferior" — it carries a capability the generic lacks.**
> `WithContentResolver<TModel>` needs a compile-time `TModel`. The `Type`-keyed form does not, so it
> can register resolvers over model types discovered at runtime (iterating a type provider or a
> registry). That is what the interface's own remark about "heterogeneous dictionaries" describes.
>
> **(c) "Bypasses the type guard" is true but harmless, and the generic's guard is dead too.**
> Dispatch in `HtmlResolver.ResolveEmbeddedContentAsync` reads `modelType` off the content's own
> `IEmbeddedContent<X>` interface and then looks that up, so the delegate only ever runs with content
> whose model type equals the key. `content is IEmbeddedContent<TModel>` is true by construction and
> the generic version's `: ValueTask.FromResult(string.Empty)` fallback is unreachable. The
> `Type`-keyed form is not missing a safety net; there was never anything to catch.
>
> **What is actually wrong** is ergonomic and editorial. The delegate is `Func<IEmbeddedContent, string>`,
> so callers write a cast plus an `else` that can never run — and `README.md:1094` and
> `docs/upgrade-guide.md:1026` teach that as *the* "Batch Resolver Registration" example, which is bad
> teaching for the common compile-time-types case. (The original claim that the README and upgrade
> guide document the *codename*-keyed pair is backwards: codename-keyed appears only in
> `docs/rich-text-customization.md`, at `:91` as a dictionary and at `:465` under the heading
> "Codename-Based Tuple Resolvers (Legacy)".) A third gap the draft missed: neither bulk overload has
> a `ValueTask` variant, while both single registrations do.
>
> **Revised action: keep all four, no break.** Collapse each byte-identical pair and rewrite the three
> doc sites to lead with `WithContentResolver<T>`, demoting the `Type`-keyed form to the
> runtime-discovered-types case it is actually for. Plus two levers that address the objection that
> fixing only prose leaves the footgun as a reachable default:
>
> 1. **Rewrite the `<remarks>` on the `Type`-keyed members of `IHtmlResolverBuilder`.** The current
>    text is the real culprit — it appears in IntelliSense at the point of use and *instructs* the dead
>    code: "Use pattern matching inside the resolver for type-safe access:
>    `content switch { IEmbeddedContent<Article> a => ..., _ => "" }`". Replace it with: for model
>    types known at compile time use `WithContentResolver<TModel>`; this overload exists for types
>    discovered at runtime. This does more than all three doc-site rewrites combined.
> 2. **`[EditorBrowsable(EditorBrowsableState.Advanced)]`** on the two `Type`-keyed overloads. They
>    leave the default completion list, stay fully callable, and nobody gets a compile error.
>
> Note that the dead-code pattern is not forced by the signature. A caller who genuinely has only a
> runtime `Type` cannot write `is IEmbeddedContent<Tweet>` at all, and writes no cast; the pattern
> appears only when the runtime-types overload is used for compile-time types, for which the typed
> route is already shorter per entry.
>
> **Deliberately not done:** adding a singular `WithContentResolver(Type, Func<IEmbeddedContent, ValueTask<string>>)`.
> It does not exist today, which is precisely why the bulk pair cannot be deprecated — there would be
> nothing to point at. Adding it would create an honest `[Obsolete]`-in-20.x, remove-in-21.0.0 runway,
> and would close the gap that neither bulk overload has a `ValueTask` variant while both single
> registrations do. Revisit only if retiring the pair is actually intended; adding a member to justify
> a deletion that may never come is the speculative API CLAUDE.md rules out.
>
> **Gate gap found while evaluating this:** `src/testing/PublicApiApproval.cs` emits no attributes
> except `required`, so neither `[EditorBrowsable]` nor a future `[Obsolete]` shows up in an approval
> snapshot. Deprecations are consumer-visible but invisible to the gate — the changelog has to carry
> them.

## 4. Dead code, unreachable branches, and comments that are wrong

- **`RichTextContent.Links` / `.Images` / `.ModularContentCodenames` have no production reader.** All
  three are assigned by the parser and read by exactly one test
  (`RichTextElementEnvelopeReaderTests.cs:98-100`), which asserts the parser copied the reader's
  output. Their doc comments say "Internal property used during HTML resolution" — this is false:
  `HtmlResolver` takes `IRichTextContent`, which exposes only `Count`, the indexer and the
  enumerator, and therefore cannot see them.

  Resolution *does* handle links and images; it gets them **per block, baked in at parse time**, and
  the confusion is that two different types carry a `.Links`/`.Images` pair. The one that matters is
  the envelope (`IRichTextElementValue` / `RichTextElementData`):

  | Data | Sourced at | Reaches the resolver as |
  |---|---|---|
  | Link metadata | `RichTextParser.cs:139` reads `elementValue.Links[itemId]` | `ContentItemLink.Metadata`, read at `HtmlResolver.cs:81` and `DefaultResolvers.cs:38-47` |
  | Inline image | `RichTextParser.cs:215` reads `element.Images[assetId]` | the `IInlineImage` *is* the block; the default resolver reads `Url`/`Description`/`ImageId` |
  | Embedded content | `getLinkedItem(codename)` | the resolved `IEmbeddedContent` *is* the block |

  So `RichTextContent`'s copies are a second, container-level snapshot of the same three collections,
  assigned once at `RichTextParser.cs:55` and consumed by nothing. Removing them is free — they are
  `internal`, so no public surface changes, and `GetInlineImages()` already offers the container-level
  view by walking blocks. The only thing the block walk cannot see is an entry present in the API's
  `images`/`links` collection with no matching `<figure>`/`<a>` in the HTML; nothing uses that today,
  so it is a latent capability rather than a feature. If it is wanted, expose it deliberately rather
  than leaving it as unread state.
- **`HtmlResolver.ResolveHtmlNodeAsync` step 3 is unreachable.** `Build()` always assigns
  `DefaultHtmlNodeResolver` (a ternary with `DefaultResolvers.HtmlElementResolver()` as its
  fallback), and `HtmlResolver` is constructed nowhere else. If it were reachable it would allocate a
  fresh delegate per node.
- **A stale comment sits above no field**: `// Performance cache: maps tag names to their dedicated
  resolvers for O(1) lookup` — the field it described is gone.
- **`HtmlResolverOptions.ThrowOnMissingResolver` doc is wrong**: it says "silently skips blocks
  without resolvers"; the code emits an HTML comment naming the missing resolver.
- **`DefaultResolvers.BuildAttributes` has a redundant overload** — the `params` version already
  covers the no-extra-attributes case.
- **`RichTextParser.ConvertAsync<TElement>`'s generic is decorative**: constrained to
  `IContentElementValue<string>`, then immediately `if (contentElement is not IRichTextElementValue) return null`.
  A test (`ConvertAsync_NonRichTextElement_ReturnsNull`) exists only to cover the hole the signature
  opens. Both production callers pass a `RichTextElementData`.
- **`options is null ? Deserialize(x) : Deserialize(x, options)` appears three times** in the envelope
  reader (`:49`, `:78`, `:104` — the draft said four). Verified by probe that the two-argument overload
  accepts a null `JsonSerializerOptions`, so all three ternaries are noise.

## 5. Performance

- **Uncached reflection in the render path.** `HtmlResolver.TryGetEmbeddedContentModelType` calls
  `content.GetType().GetInterfaces()` — which allocates an array — for **every embedded block on
  every resolve**, to find the `IEmbeddedContent<>` argument. CLAUDE.md's rule is to cache anything
  reflective; a `ConcurrentDictionary<Type, Type?>` is the minimum, and exposing the model type off
  the item would remove the reflection entirely.
- `HtmlEncoder.Create(UnicodeRanges.All)` is constructed on every `Build()` rather than once.
- The `Count > 0 ? dict : null` in `Build()` paired with `?? FrozenDictionary.Empty` in the
  constructor adds branching that changes no outcome.

## 6. Suggested sequencing

1. **§2.1** — settle both parameters at the reader instead of at the callers. Give
   `RichTextElementEnvelopeReader` one private static `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`
   and delete both optional parameters. This is stronger than "pass the shared options from
   `ElementValueMapper`": the envelope's three shapes are internal records with explicit
   `[JsonPropertyName]` attributes that need nothing from user-configured options, so making the two
   paths identical *by construction* beats making them identical only as long as both callers remember
   to pass the same instance — which they would not be, since the dynamic path holds its own private
   `RichTextEnvelopeReadOptions`. It also removes the `preserveEmpty` fork and the three ternaries in
   the same stroke. Trade-off, stated deliberately: a user's custom converter registered via
   `DeliveryJsonOptions` would then never reach inline images or content links. That is already the
   dynamic path's behaviour and nobody has asked for the seam.
2. **§2.3** — move `ThrowOnMissingResolver` onto `IHtmlResolverBuilder` returning
   `IHtmlResolverBuilder`. One line of interface, one signature change.

   **Correction to "the whole plan is non-breaking".** This step is the exception, and the claim
   overreached. Adding a member to the public `IHtmlResolverBuilder` breaks any external implementer
   at compile time, and narrowing `HtmlResolverBuilder.ThrowOnMissingResolver`'s return type from
   `HtmlResolverBuilder` to `IHtmlResolverBuilder` breaks code that assigns the result to the concrete
   type. Both are theoretical rather than likely — `Build()` returns `IHtmlResolver`, whose only
   implementation is `internal`, so an external implementer of the builder interface would have to
   reimplement the resolver too — but the approval snapshot *will* gain a line, and that line should
   be reviewed as an intended change rather than waved through. The alternative that avoids the
   return-type half (explicit interface implementation alongside the concrete method) buys little and
   costs clarity; not taken.
3. **§2.4** — fix the comment, and decide whether one encoder should be used throughout.
4. **§3.1** — three typed fields replacing the `Delegate` dictionary. Internal only; contained.
5. **§4** — the dead branch, dead state, stale comments and the four ternaries.
6. **§2.2 and §3.2** — documentation and deduplication only. Both were re-examined and neither
   warrants a break (see the corrections in place): fix the `GetEmbeddedContentOfType` XML example and
   document depth on both methods; collapse the identical `WithContentResolvers` pairs and rewrite the
   three doc sites to lead with `WithContentResolver<T>`. **With this, the whole plan is
   non-breaking** — no entry in the public API approval snapshots changes.

## 7. Checked and correct — do not re-open

`DefaultResolvers.IsVoidElement` recognises only `br` and `img`. This was raised during analysis as a
possible gap and confirmed correct: those are the only void elements Kontent.ai rich text emits, so
there is no third case to add and no element that renders with a bogus closing tag.

## 8. Not examined

`Kontent.Ai.Delivery.Caching` internals remain untraced — still the last unexamined area named by the
query-builder document.
