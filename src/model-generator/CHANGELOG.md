# Kontent.Ai.ModelGenerator

Covers `Kontent.Ai.ModelGenerator` (the `dotnet tool` CLI) and its
`Kontent.Ai.ModelGenerator.Core` library, which ship in lockstep on one version.

All notable changes to these packages are documented here.
Entries before the move to this monorepo were imported from the GitHub Releases of
[kontent-ai/model-generator-net](https://github.com/kontent-ai/model-generator-net).

## Unreleased

Targets .NET 10. Both packages move from `net8.0` to `net10.0`, which is why this is a major release rather than a continuation of the `10.3.0` line. Generated output is unchanged.

### Breaking changes

- **`net8.0` → `net10.0`.** `Kontent.Ai.ModelGenerator.Core` is a library, so a project on .NET 8 cannot reference this release at all — restore fails with `NU1202`. The `Kontent.Ai.ModelGenerator` CLI likewise needs the .NET 10 runtime to run. Move to .NET 10 first.
- **Two generator base-class properties became methods.** `ClassCodeGenerator.Properties` is now `GetProperties()`, and the Delivery generator's `PropertyCodenameConstants` is now `GetPropertyCodenameConstants()`. Both re-sort their input and build a fresh set of Roslyn syntax nodes on every access, so a property was misleading about the cost — two reads returned two different arrays. `GetProperties()` remains `virtual`, so overriding it still works; a derived generator changes `override … Properties` to `override … GetProperties()`. Only affects code that subclasses these base classes.
- **`--withtypeprovider` / `-t` and `CodeGeneratorOptions.WithTypeProvider` are removed**, along with the `TypeProviderCodeGenerator` that backed them. The Delivery SDK generates its own `GeneratedTypeProvider` at compile time from `Kontent.Ai.Delivery.SourceGeneration` and discovers it at runtime, so nothing needs a hand-written provider any more.

  The flag had in fact stopped doing anything before this release: the code path behind it lived on a method that hid its base rather than overriding it, and the CLI invokes the base, so passing `-t` generated no provider and printed no warning. Passing it now fails with `Unsupported parameter: -t` rather than being silently ignored. Remove it from your scripts and reference `Kontent.Ai.Delivery.SourceGeneration` from the project your models are generated into.

- **`CodeGeneratorBase.FilenameSuffix` and `GetFileClassName` are removed.** The suffix has been the empty string since single-file generation landed, which made `GetFileClassName(name)` an identity function. Generated file names are unchanged. Only affects code that subclasses `CodeGeneratorBase`.
- **`IOutputProvider.Output` returns `bool` instead of `void`** — `true` when it wrote the file, `false` when the file already existed and `overwriteExisting` was not set. The generator reports each file's outcome and had no way to tell the two apart. Only affects code that implements `IOutputProvider`; a custom implementation adds a `return true;`.
- **The dropped custom-partial emission path is gone.** `PartialClassCodeGenerator`, the `customPartial` flag on `IClassCodeGeneratorFactory.CreateClassCodeGenerator`, and `ClassCodeGenerator.OverwriteExisting` all existed to support emitting a second, user-extensible partial file. The CLI never asked for it — the flag was never passed as `true` — so the generator was unreachable, and `OverwriteExisting` was a `GetType() != typeof(PartialClassCodeGenerator)` check that could only ever answer `true`. The factory method also took an `IUserMessageLogger` it null-checked and never used; that parameter is gone too.
- **`IDeliveryElementService` and `DeliveryElementService` are removed.** `GetElementType(string)` returned its argument unchanged, and the injected options were never read — an interface, an implementation, a DI registration and an inheritance layer computing the identity function. `DeliveryCodeGenerator` now reads `element.Value.Type` directly and derives from `CodeGeneratorBase`; `DeliveryCodeGeneratorBase`, whose only purpose was carrying the service, is gone with it.
- **The always-true emission seams are gone.** `ClassCodeGenerator.IsRecord` and `UseFileScopedNamespace` were `virtual` and defaulted to `false`, but every concrete generator overrode both to `true`, so the class-emitting and block-namespace branches were unreachable. `DeliveryClassCodeGeneratorBase` had one subclass left after the custom-partial removal and is folded into `DeliveryClassCodeGenerator`, which is now `sealed`.
- **Dead public members are removed from `Property` and `TextHelpers`.** On `Property`: `ObjectType`, `IsNullable`, `HasInitializer`, the already-obsolete `RequiresDefaultInitializer`, and the `IsDateTimeElementType` / `IsRichTextElementType` / `IsModularContentElementType` predicates — none reachable from any emission path. On `TextHelpers`: `GetEnumerableType`, and `GetUpperSnakeCasedIdentifierName`, which despite its name produced `Pascal_Snake_Case` rather than upper snake case and was called by nothing.

  **Generated output is unaffected.** Verified by generating against a live environment before and after: all 15 files, including the `--baserecord` extender, are byte-identical.
- **`ClassDefinition.AddPropertyCodenameConstant` is removed; `AddProperty` now registers both the property and its codename constant.** The two were always called as a pair, and calling them separately is what allowed a rejected property to leave its constant behind. `CodeGeneratorBase.AddProperty(Property, ref ClassDefinition)`, which wrapped the pair and had no callers, is removed with it. Only affects code that drives `ClassDefinition` directly.

### Changed

- **Arguments are validated against the SDKs' own rules instead of a hand-written subset.** The tool checked only that an environment id was present and non-blank, so `-i not-a-guid` was accepted and the run failed later against the API with a less obvious message. It now runs the validation the SDK options already declare — data annotations plus `IValidatableObject` — which is what the SDKs' own container-free constructors do. Every problem is reported at once rather than the first, so a run started with several bad arguments does not have to be repeated once per mistake.

  ```text
  $ KontentModelGenerator -i not-a-guid
  The delivery configuration is not valid:
    - EnvironmentId: The environment ID must be a valid GUID.
  See http://bit.ly/k-params for more details on configuration.
  ```

  Configurations that were valid before remain valid. A configuration the tool used to accept and the API would then reject now fails at startup.
- **`--baserecord` no longer fetches the content model twice.** Generating the base record re-read the whole content model rather than reusing what had just been fetched, so a run with `-b` made every request twice — in management mode, both the content-type and snippet listings. The generated output was identical either way; only the number of API calls changes.
- **Nothing about the code the generator emits**, for any content model that generated valid code before. Model classes, enums and the mapping attributes are byte-identical to `10.3.0-beta-2`, verified by the generator's own output assertions. Models that previously came out uncompilable are covered under Fixed.

### Fixed

- **Element codenames that differ but produce the same C# identifier no longer emit uncompilable models.** Duplicate detection compared raw codenames while emission used the PascalCased identifier, so `my_element` and `my__element` — two codenames, one identifier — both got through. The generated record then declared `MyElementCodename` twice and did not compile. The same hole existed between the two kinds of member: an element named `title` and one named `title_codename` produced a constant and a property that were both called `TitleCodename`, and that case emitted no warning at all. Everything a record declares is now checked against one registry of identifiers, and the offending element is skipped with a message naming both codenames and the identifier they collide on.

  ```text
  Warning: Skipping element 'my__element'. Content type 'article': 'my__element' and 'my_element'
  both produce the identifier 'MyElement'. Rename one of the elements in Kontent.ai.
  ```

  A rejected element no longer half-registers either — the constant used to be recorded before the property could be refused, so skipping one element still corrupted the output.
- **An element that fails for an unanticipated reason is now reported instead of vanishing.** Per-element failures were classified by a `switch` with arms for the three expected exception types and no default, so anything else was caught, matched nothing, and left the element out of the generated model with nothing written to the console.
- **The tool no longer claims to have created a base record it did not write.** `--baserecord` deliberately does not overwrite an existing file, so hand-written additions survive a rerun — but the run printed "`<name>` class was successfully created" either way. It now says the file was kept, and `IOutputProvider.Output` returns whether it wrote (see Breaking changes).
- **The "no content type available" message names the environment in management mode.** It read the Delivery options only, so a `--managementapi` run against an empty environment reported the id as blank.
- **A failure with more than one inner exception no longer exits silently.** `Main` had a special case for `AggregateException` that printed the message only when there was exactly one inner exception and otherwise returned exit code 1 with no output at all. `await` unwraps these anyway, so the case was vestigial; it is removed and the general handler reports every failure.
- **Two content types that map to the same file no longer silently overwrite each other.** Type codenames sanitize to a class name the same way element codenames do, so `my_type` and `my__type` both wrote `MyType.cs` — the second overwrote the first, and the run reported both as created. The duplicate is now skipped with a warning, and the "N content type models were successfully created" count reflects what was actually written.

### Dependencies

Shipped floors moved up:

- `Microsoft.CodeAnalysis` **4.13.0** → **5.6.0**, aligning the generator with the Roslyn version the rest of the repo builds against.
- `Microsoft.Extensions.*` (`Options`, `Configuration.CommandLine`, `Configuration.Json`, `DependencyInjection`) **9.0.15** → **10.0.10**.

### Internal

No consumer-visible effect:

- **The generator's output no longer reaches for `Console` from three different places.** `UserMessageLogger` now takes the writers it should use, defaulting to standard output and standard error, and the two places that bypassed it - command-line validation and the SDK-version banner - go through it. Command-line validation returns the problems it finds rather than printing them, since it runs before there is any container to resolve a logger from. Terminal output is unchanged, character for character.

  This also lets the tool's test assembly run in parallel again. It had been forced sequential because one test captured output by reassigning `Console.Out`, which is process-wide, and collided with another test writing to `Console.Error`.
- A transient `HttpClient` was registered in delivery mode and resolved by nothing. `AddDeliveryClient` builds its transport through `IHttpClientFactory`, so the registration was inert - and had anything picked it up, it would have bypassed the SDK's whole handler chain: no authentication, no tracking headers, no resilience.
- Three JSON fixtures under the Core test project were copied to the build output and referenced by no test. The tests construct their inputs in memory instead. Recoverable from history if realistic payloads are wanted later.
- Identifier-sanitizing regular expressions are compiled at build time and carry an execution timeout. Codenames arrive from the environment's content model, so they are external input, and a generator that hangs on one malformed codename is worse than one that fails.
- **`Kontent.Ai.ModelGenerator.Options` is now `Kontent.Ai.ModelGenerator.CommandLine`.** The namespace holds command-line argument handling — `ArgHelpers`, `ArgMappingsRegister`, `UsedSdkInfo`, `ValidationExtensions` — and nothing to do with `IOptions`. Sitting directly under `Kontent.Ai.ModelGenerator`, it shadowed `Microsoft.Extensions.Options` throughout the assembly and both test projects, so `Options.Create(...)` bound to the namespace and had to be written fully qualified to compile. Only the CLI tool assembly is affected: `Kontent.Ai.ModelGenerator.Core` has no such namespace, and the tool is installed rather than referenced.


## 10.3.0-beta-2 (2026-08-04)  _(prerelease)_

### Improvements

- Moved into the [kontent-ai/dotnet](https://github.com/kontent-ai/dotnet) monorepo. Package IDs and the `dotnet tool` install command are unchanged.
- The `<auto-generated>` header on generated files now links to the monorepo rather than the archived `model-generator-net` repository. This is the only change to generated output — regenerating produces a one-line diff per file.
- Nullable reference types are now enabled. Signatures across the generator were corrected to state what they already did — most visibly `ManagementElementMetadataAdapter.ToInput`, which returns `null` for element types the generator does not emit yet (its caller has always checked for null) but declared a non-nullable return. Configuration properties that are only populated in one of the two modes (`DeliveryOptions`, `ManagementOptions`) and the genuinely optional ones (`Namespace`, `OutputDir`, `BaseRecord`) are now nullable.

### Fixes

- The Delivery type-provider path no longer passes a null document to the output provider when no content types were registered.
- A missing element `codename` or `id` in a Management API response now fails with a message naming the element, instead of producing a model that does not compile.
- Restore no longer fails with NU3012. Refit now resolves to 10.2.0 — the 10.1.6 packages are signed with a revoked certificate. 10.3.0-beta-1 resolved to 10.1.6 through both `Kontent.Ai.Delivery` 19.3.0 and `Kontent.Ai.Management` 9.0.0-beta-1, so installing it on a clean package cache failed. Fixed at the source rather than pinned here: both SDKs now ask for 10.2.0.

### Dependencies

- **`Kontent.Ai.Management` `9.0.0-beta-1` → `9.0.0-beta-5`.** Management model emission follows that SDK's beta-3 rename of `SnippetIdentifier` to `Snippet`, which unified identifier-pair properties on the bare style. The wire format is unchanged.
- **`Kontent.Ai.Delivery` `19.3.0` → `19.4.0`.** Delivery model generation is unaffected.
- **`Microsoft.Extensions.Options` `10.0.1` → `9.0.15`.** A deliberate step back onto the 9.x line: the packages target `net8.0`, and the whole `Microsoft.Extensions.*` band is pinned together across the monorepo. 10.0.1 had drifted in ahead of the runtime it belongs to. The band moves forward as a unit when the SDKs adopt .NET 10.

## 10.3.0-beta-1 (2026-06-26)  _(prerelease)_

This is a beta release that **re-introduces Management API model generation** — removed in 10.0.0 — now built against the modernized Management SDK (`Kontent.Ai.Management 9.0.0-beta-x`). Delivery generation is unchanged; Management is an additive, opt-in mode (`-m` / `--management`).

It ships as a **beta** because the Management SDK it targets is itself a beta and the generated Management model shapes may still change before this mode stabilizes.

For details on the 10.2.0 changes, see the [10.2.0 release notes](https://github.com/kontent-ai/model-generator-net/releases/tag/10.2.0).

> [!IMPORTANT]
> This is a **Delivery-plus-Management** preview built on the modern SDKs. If you need Management models on the **legacy** Management SDK, use the [9.0.0 release](https://github.com/kontent-ai/model-generator-net/tree/9.0.0). For Delivery-only generation, the current stable release is [10.2.0](https://github.com/kontent-ai/model-generator-net/releases/tag/10.2.0) — its behavior is unchanged here.

### New features

- **Management mode (`-m` / `--management`)** — generates Management content-type models from a project's content model. Each content type is emitted as a `sealed partial record` implementing `IElementsModel`, with:
  - `[KontentType]` at the type level and `[KontentElement]` per property;
  - constraint attributes mapped from the content model — `[StringLength]`, `[RegularExpression]`, `[MinElements]` / `[MaxElements]` / `[ExactElements]`, `[AllowedTypes]`, `[AllowedItemLinkTypes]`, `[AllowedTaxonomyGroup]`, `[MaxAssetSize]`, and `[AllowedAssetFileTypes]`;
  - strongly-typed value wrappers — `DateTimeValue?`, `UrlSlugValue?`, `CustomValue?`, and `RichTextValue?` — for the `date_time` / `url_slug` / `custom` / `rich_text` elements that carry a field beside `value` on the wire;
  - reference collections as `IEnumerable<Reference>?` (linked items, subpages, taxonomy) and `IEnumerable<AssetReference>?` (assets);
  - multiple-choice elements as `IEnumerable<TEnum>?` with a sibling `enum` whose members carry `[KontentEnumValue]`;
  - content type snippets expanded inline (snippet-prefixed codenames pass through verbatim).

### Changes

- **Management SDK bumped `8.2.0` → `9.0.0-beta`** — the generator now consumes and emits against the modernized client:
  - registers the client through the new `AddManagementClient` DI extension (replacing the hand-built `ManagementClient`), mirroring `AddDeliveryClient`;
  - adopts the result pattern — listings return `IManagementResult<IReadOnlyList<T>>` and never throw on API errors; a failed listing aborts generation with a readable message;
  - listings are materialized (no continuation-token paging to walk).
- **`[AllowedAssetFileTypes]` now takes `FileType`** (`Any` / `Adjustable`) from `Kontent.Ai.Management.Models.Types.Elements`, replacing the deleted `AssetFileType` enum and its bogus `Image` value.
- **`rich_text` emits `RichTextValue`** (renamed from `RichTextElement`), keeping the value family consistent with `DateTimeValue` / `UrlSlugValue` / `CustomValue`.
- **Generated collection properties emit `IEnumerable<T>`** (not `IReadOnlyList<T>`) — generated records are dual-use and the Management SDK is write-primary, so collections are optimized for the write path.

### Dependencies

- **Delivery SDK bumped `19.2.0` → `19.3.0`** to align both SDKs on **Refit 10.1.6**. The modernized Management SDK pins Refit 10.1.6 while Delivery 19.2.0 pinned Refit 8.0.0; referencing both pulled two Refit majors into one closure (a binding conflict). Delivery 19.3.0 shipped the Refit 10.1.6 alignment, resolving it. Delivery model generation is otherwise unaffected.
- **`Microsoft.Extensions.*` bumped to `9.0.3`** to match the transitive floor pulled in by the aligned Refit/HTTP stack.

### Looking ahead

Management mode and its generated shapes will be finalized alongside the stable `Kontent.Ai.Management` release. Pin a specific `10.3.0-beta-*` build if you depend on the current Management output while it stabilizes.

## 10.2.0 (2026-05-05)

This is a minor release that adds an opt-in `--nullability semantic` mode for generating non-nullable element properties with sensible defaults, and emits `#nullable enable` on every generated content type record.

For details on the 10.1.1 changes, see the [10.1.1 release notes](https://github.com/kontent-ai/model-generator-net/releases/tag/10.1.1).

### New features

- **`--nullability` flag** — controls how element properties express nullability. The default `strict` keeps every property nullable (`string?`, `RichTextContent?`, `IEnumerable<Asset>?`), matching previous behavior. The new `semantic` mode matches the runtime semantics of the Delivery API: text, rich text, and collection elements always come back populated, so they're emitted as non-nullable with default initializers (`= string.Empty`, `= RichTextContent.Empty`, `= []`). Numbers, dates, and custom elements remain nullable. See [Nullability mode](https://github.com/kontent-ai/model-generator-net#nullability-mode) for tradeoffs (notably with projection).
- **`#nullable enable` directive in generated content type records** — generated record files now open with `#nullable enable`, so the `?` annotations are honored regardless of the consuming project's nullability context. Projects without a project-wide nullable context no longer warn (CS8632) on the generated annotations.

> [!IMPORTANT]
> This version requires you to bump delivery SDK to at least version 19.2.0, which introduced `RichTextContent.Empty`.

### Deprecations

- **`Property.RequiresDefaultInitializer` is now `[Obsolete]`** — replaced by `Property.HasInitializer` (and the new `Property.Initializer` expression). Only relevant if you consume `Kontent.Ai.ModelGenerator.Core` as a library; the CLI is unaffected.

### Looking ahead

`--nullability semantic` will become the default in the next major version. If your code branches on `null` to detect projection-omitted elements (`WithElements` / `WithoutElements`), pin `--nullability strict` explicitly when you upgrade to keep that distinction in the type system.


### What's Changed
* Feat/default values by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/204


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.1.1...10.2.0

## 10.1.1 (2026-04-23)

Fixes `ContentTypeCodename` on generated records to be a compile-time constant instead of an instance property.

For details on the 10.1.0 changes, see the [10.1.0 release notes](https://github.com/kontent-ai/model-generator-net/releases/tag/10.1.0).

### Changes

- **`ContentTypeCodename` is now a `public const string`** — previously emitted as an expression-bodied instance property (`public string ContentTypeCodename => "article";`), which required allocating an instance to read the value and could not be used in contexts that require a compile-time constant. It now emits `public const string ContentTypeCodename = "article";`, enabling use in `switch`/`case` labels (e.g., dispatching on `item.System.Type`), attribute arguments, and static lookup tables. Collision handling (renaming conflicting element members to `_ContentTypeCodename`) is unchanged.

### Migration

- Call sites that accessed `ContentTypeCodename` through an instance reference (e.g., `article.ContentTypeCodename`) will no longer compile. Access it through the type name instead (`Article.ContentTypeCodename`).


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.1.0...10.1.1

## 10.1.0 (2026-04-21)

This is a minor release that adds the `ContentTypeCodename` property and fixes the base class parameter to generate records instead of classes.

For details on v10 changes, see the [10.0.0 release notes](https://github.com/kontent-ai/model-generator-net/releases/tag/10.0.0).

### New features

- **`ContentTypeCodename` property** — each generated record now includes an expression-bodied `ContentTypeCodename` property for accessing the content type codename directly without reflection
- **Collision handling** — elements whose codename would collide with the built-in `ContentTypeCodename` property or constant (e.g., `content_type_codename`, `content_type`) are automatically prefixed with `_` to avoid conflicts

### Fixes

- **`--baseClass` renamed to `--baseRecord`** — the previous `--baseClass` parameter generated a base class, but since models are now records, this caused a compilation error. The parameter now generates a base record. `--baseClass` (`-b`) still works as an obsolete alias. A new `-r` short key was added as an alternative.


### What's Changed
* Add missing type codename, fix baseClass behavior by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/202


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.0.0...10.1.0

## 10.0.0 (2026-04-19)

This is the first stable release of the model generator targeting the **modern Delivery SDK v19+**.

Generated models now use **records**, **nullable properties**, **`ContentTypeCodename` attribute** for source-generated TypeProvider discovery, and **element codename constants** for compile-time query building.

### Breaking changes from 9.0.0

> [!IMPORTANT]
> This version generates **Delivery models only**. If you need Management SDK models, Extended Delivery models, or models for Delivery SDK v18.x and earlier, use the [previous stable release (9.0.0)](https://github.com/kontent-ai/model-generator-net/tree/9.0.0).

- **Delivery SDK v19+ required** — generated models use types from the new SDK (`RichTextContent`, `DateTimeContent`, `IEmbeddedContent`, `Asset`, etc.) and are not compatible with v18.x and earlier
- **Records instead of classes** — generated models are immutable `record` types with `{ get; init; }` accessors
- **All properties are nullable** — correctly reflects projection scenarios (`WithElements` / `WithoutElements`) where omitted properties are `null` at runtime
- **Removed Management SDK support** — the `--apikey` / `-k` parameter (Management API key) and Management API model generation have been removed. Note: Secure Delivery API access is unaffected — use `--DeliveryOptions:UseSecureAccess true --DeliveryOptions:SecureAccessApiKey <key>` or `appSettings.json` as before
- **Removed Extended Delivery models** — the `--extendeddeliverymodels` parameter has been removed
- **Removed `--structuredmodel` parameter** — structured model variants are no longer applicable
- **Removed `--withtypeprovider` flag** — TypeProvider is now source-generated by the Delivery SDK via the `ContentTypeCodename` attribute; no custom `ITypeProvider` class is generated
- **Removed `--filenamesuffix` parameter**

### Changes since beta-5

- Removed all remaining Management API-related code paths, configuration options, and test fixtures
- Updated Delivery SDK dependency from `19.0.0-rc5` to the production `19.0.0` release
- Updated documentation and README

#### Full list of changes since beta-5

- Removed `ServiceCollectionExtensions` (MAPI DI registrations)
- Removed `DesiredModelsType` and `StructuredModelFlags` configuration types
- Removed MAPI-related properties from `ClassDefinition` and `Property`
- Cleaned up `appSettings.json` defaults
- Removed legacy docs folder and assets
- Updated README to reflect Delivery-only scope

### What's Changed
* Delivery modernization by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/199


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/9.0.0...10.0.0

## 10.0.0-beta-5 (2026-03-03)  _(prerelease)_

#### Changes from previous beta

- Changed all generated delivery model properties to nullable types to correctly reflect projection scenarios (`WithElements` / `WithoutElements`) where omitted properties are `null` at runtime
##### Beta 4 changes
- Reintroduced element codename constants to generated delivery models (e.g. `public const string TitleCodename = "title"`) for compile-time query/filter building
- Fixed duplicate `--environmentid` / `--environmentId` parameter mapping that caused conflicts
- Made CLI parameter matching case-insensitive — `--environmentId`, `--environmentid`, and `--ENVIRONMENTID` are now all valid

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.0.0-beta-3...10.0.0-beta-5

## 10.0.0-beta-4 (2026-03-03)  _(prerelease)_

#### Changes from previous beta

- Reintroduced element codename constants to generated delivery models (e.g. `public const string TitleCodename = "title"`) for compile-time query/filter building
- Fixed duplicate `--environmentid` / `--environmentId` parameter mapping that caused conflicts
- Made CLI parameter matching case-insensitive — `--environmentId`, `--environmentid`, and `--ENVIRONMENTID` are now all valid
- Changed `AllMappingsKeys` from `IEnumerable<string>` to `ISet<string>` with case-insensitive comparison for consistent parameter validation

For full feature list and installation instructions, see [10.0.0-beta-3 release notes](https://github.com/kontent-ai/model-generator-net/releases/tag/10.0.0-beta-3).

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.0.0-beta-3...10.0.0-beta-4

## 10.0.0-beta-3 (2026-02-20)  _(prerelease)_

#### Changes from previous beta

- Updated Delivery SDK dependency from `19.0.0-beta-4` to `19.0.0-rc1`
- Added `[ContentTypeCodename("codename")]` attribute to generated records for source-generated TypeProvider discovery (requires `Kontent.Ai.Delivery.SourceGeneration` package)
- Added `using Kontent.Ai.Delivery.Attributes;` to generated model usings
- Marked `WithTypeProvider` option as obsolete — TypeProvider is now generated via source generation in Delivery SDK 19.0.0-rc1+
- Changed `--withtypeprovider` default from `true` to `false`
- Updated `Microsoft.Extensions.Options` dependency from `9.0.8` to `10.0.1`

 ### 🚀 Beta Release - Modern Delivery SDK (v19+) Support

  This is a beta release that has been completely modernized to work exclusively with the Kontent.ai Delivery SDK
  for .NET v19.0.0-rc1 and higher.

#### ✅ What's Included

  - Modern record-based model generation for Delivery SDK v19+
  - File-scoped namespaces, { get; init; } accessors
  - JSON property attributes for explicit mapping
  - Modern concrete types (RichTextContent, Asset, TaxonomyTerm, IEmbeddedContent)
  - Single file generation per model (no .Generated.cs splits)
  - Partial records for easy extensibility
  - `[ContentTypeCodename]` attribute on generated records for automatic TypeProvider registration

 ### ❌ What's NOT Included

  This beta does not support:
  - Legacy Delivery SDK (v18.x and earlier)
  - Management SDK model generation
  - Extended Delivery model generation

 ### 📦 Who Should Use This Release?

 #### ✅ Use this beta if:
  - You're using the new Delivery SDK v19.0.0-rc1 or higher
  - You want modern C# record-based models

#####  ❌ Use the https://github.com/kontent-ai/model-generator-net/releases if:
  - You're using legacy Delivery SDK (v18.x or earlier)
  - You need Management SDK models
  - You need Extended Delivery models

  ### 🔧 Breaking Changes

  - `--withtypeprovider` now defaults to `false` — the Delivery SDK 19.0.0-rc1+ generates its own TypeProvider via source generation
  - Requires Delivery SDK v19.0.0-rc1+ — incompatible with earlier versions

  ### 📝 Installation

  # Global tool
  dotnet tool install -g Kontent.Ai.ModelGenerator --version <version>

  # Usage
  KontentModelGenerator --environmentid "<environmentId>" --namespace "<namespace>" --outputdir "<output>"

  ---
  This beta release enables early adopters to test the modernized model generation alongside the RC Delivery
  SDK. Full public release will follow once the Management SDK is also modernized.


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.0.0-beta-2...10.0.0-beta-3

## 10.0.0-beta-2 (2026-01-11)  _(prerelease)_

#### Changes from previous beta

- Changed `date_time` element type from `DateTime?` to `DateTimeContent` (from `Kontent.Ai.Delivery.SharedModels`)
- Removed `IElementsModel` interface implementation from generated records (interface no longer exists in SDK v19 beta 3)
- Updated auto-generated comments to use "record" terminology instead of "class"


 ### 🚀 Beta Release - Modern Delivery SDK (v19+) Support

  This is a beta release that has been completely modernized to work exclusively with the Kontent.ai Delivery SDK 
  for .NET v19.0.0-beta-3 and higher.

#### ✅ What's Included

  - Modern record-based model generation for Delivery SDK v19+
  - File-scoped namespaces, { get; init; } accessors
  - JSON property attributes for explicit mapping
  - Modern concrete types (RichTextContent, Asset, TaxonomyTerm, IEmbeddedContent)
  - Single file generation per model (no .Generated.cs splits)
  - Partial records for easy extensibility

 ### ❌ What's NOT Included

  This beta does not support:
  - Legacy Delivery SDK (v18.x and earlier)
  - Management SDK model generation
  - Extended Delivery model generation

 ### 📦 Who Should Use This Release?

 #### ✅ Use this beta if:
  - You're using the new Delivery SDK v19.0.0-beta-3 or higher
  - You want modern C# record-based models

#####  ❌ Use the https://github.com/kontent-ai/model-generator-net/releases if:
  - You're using legacy Delivery SDK (v18.x or earlier)
  - You need Management SDK models
  - You need Extended Delivery models

  ###🔧 Breaking Changes

  - All Management and ExtendedDelivery code generation has been removed
  - Obsolete configuration options removed (-m, -e, -f, -g, -s)
  - Requires Delivery SDK v19+ - incompatible with v18.x

  ###📝 Installation

  # Global tool
  dotnet tool install -g Kontent.Ai.ModelGenerator --version <version>

  # Usage
  KontentModelGenerator --environmentid "<environmentId>" --namespace "<namespace>" --outputdir "<output>"

  ---
  This beta release enables early adopters to test the modernized model generation alongside the beta Delivery
  SDK. Full public release will follow once the Management SDK is also modernized.

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/10.0.0-beta...10.0.0-beta-2

## 10.0.0-beta (2025-10-26)  _(prerelease)_

### 🚀 Beta Release - Modern Delivery SDK (v19+) Support

  This is a beta release that has been completely modernized to work exclusively with the Kontent.ai Delivery SDK 
  for .NET v19.0.0-beta-2 and higher.

#### ✅ What's Included

  - Modern record-based model generation for Delivery SDK v19+
  - File-scoped namespaces, { get; init; } accessors
  - JSON property attributes for explicit mapping
  - Modern concrete types (RichTextContent, Asset, TaxonomyTerm, IEmbeddedContent)
  - Single file generation per model (no .Generated.cs splits)
  - Partial records for easy extensibility

 ### ❌ What's NOT Included

  This beta does not support:
  - Legacy Delivery SDK (v18.x and earlier)
  - Management SDK model generation
  - Extended Delivery model generation

 ### 📦 Who Should Use This Release?

 #### ✅ Use this beta if:
  - You're using the new Delivery SDK v19.0.0-beta-2 or higher
  - You want modern C# record-based models

#####  ❌ Use the https://github.com/kontent-ai/model-generator-net/releases if:
  - You're using legacy Delivery SDK (v18.x or earlier)
  - You need Management SDK models
  - You need Extended Delivery models

  ###🔧 Breaking Changes

  - All Management and ExtendedDelivery code generation has been removed
  - Obsolete configuration options removed (-m, -e, -f, -g, -s)
  - Requires Delivery SDK v19+ - incompatible with v18.x

  ###📝 Installation

  # Global tool
  dotnet tool install -g Kontent.Ai.ModelGenerator --version <version>

  # Usage
  KontentModelGenerator --environmentid "<environmentId>" --namespace "<namespace>" --outputdir "<output>"

  ---
  This beta release enables early adopters to test the modernized model generation alongside the beta Delivery
  SDK. Full public release will follow once the Management SDK is also modernized.

## 9.0.0 (2025-04-08)

### Updates
* Targets .NET 8.0 only (⚠️breaking)
* uses `--environmentid`/`-i` argument (`--projectid`/`-p` kept for legacy purposes)

### What's Changed
* Upgrade packages & .Net version by @vincent-aviva in https://github.com/kontent-ai/model-generator-net/pull/195
* disable test parallelization by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/196

### New Contributors
* @vincent-aviva made their first contribution in https://github.com/kontent-ai/model-generator-net/pull/195

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.4.0...9.0.0

## 8.4.0 (2023-12-12)

### What's Changed
* Update kontent.ai nuget packages to latest version by @Sevitas in https://github.com/kontent-ai/model-generator-net/pull/191


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.3.3...8.4.0

## 8.3.3 (2023-06-04)

### What's Changed
* respect custom namespace setting for typed models in https://github.com/kontent-ai/model-generator-net/pull/188


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.3.2...8.3.3

## 8.3.2 (2023-05-18)

### What's Changed
* 185 fix generating guidelines element in https://github.com/kontent-ai/model-generator-net/pull/186


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.3.1...8.3.2

## 8.3.1 (2023-05-05)

### What's Changed
* Fix issues with arguments in https://github.com/kontent-ai/model-generator-net/pull/184


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.3.0...8.3.1

## 8.3.0 (2023-04-20)

### What's Changed
* track usage of generators sdk in https://github.com/kontent-ai/model-generator-net/pull/175
* extended delivery models in https://github.com/kontent-ai/model-generator-net/pull/165
    * possibility to generate strongly typed modular content (linked item, subpage) elements
    * possibility to replace  modular content's (linked item, subpage elements) type (_object_) to newly introduced _IContentItem_
* fix issues with a start up parameters in https://github.com/kontent-ai/model-generator-net/pull/165

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.2.0...8.3.0

## 8.3.0-beta.6 (2023-04-13)  _(prerelease)_

### What's Changed
* track usage of generators sdk in https://github.com/kontent-ai/model-generator-net/pull/175
* extended delivery models in https://github.com/kontent-ai/model-generator-net/pull/165

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.2.0...8.3.0-beta.6

## 8.2.0 (2023-02-09)

### What's Changed
* 172 structured datetime element support in https://github.com/kontent-ai/model-generator-net/pull/173


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.1.1...8.2.0

## 8.1.1 (2023-01-05)

### What's Changed
* Fix self contained issue in https://github.com/kontent-ai/model-generator-net/pull/171


**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.1.0...8.1.1

## 8.1.1-beta.1 (2022-10-27)  _(prerelease)_

update to latest Roang-zero1/github-upload-release-artifacts-action

## 8.1.0 (2022-10-27)

### What's Changed
* Improve user experience in https://github.com/kontent-ai/model-generator-net/pull/156
* * displays info about used SDK
* * usage of invalid parameters
* Correct management api usage example in https://github.com/kontent-ai/model-generator-net/pull/163

### New Contributors
* @mattnield made their first contribution in https://github.com/kontent-ai/model-generator-net/pull/163

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/8.0.0...8.1.0

## 8.0.0 (2022-08-04)

### What's Changed
* **💥 Breaking change!** Migration to Kontent.ai Nuget by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/155
    * Changing package name from `Kentico.Kontent.ModelGenerator` to `Kontent.Ai.ModelGenerator`
    * Changing `Kentico.Kontent.*` namespaces to `Kontent.Ai.*`
* **💥 Breaking change!** Target only .NET 6 

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/model-generator-net/pull/155

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/7.0.0...8.0.0

## 8.0.0-beta.3 (2022-08-02)  _(prerelease)_

### What's Changed
* Migration by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/155

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/model-generator-net/pull/155

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/7.0.0...8.0.0-beta.3

## 8.0.0-beta.2 (2022-08-01)  _(prerelease)_

### What's Changed
* Migration by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/155

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/model-generator-net/pull/155

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/7.0.0...8.0.0-beta.2

## 8.0.0-beta.1 (2022-08-01)  _(prerelease)_

### What's Changed
* Migration by @pokornyd in https://github.com/kontent-ai/model-generator-net/pull/155

### New Contributors
* @pokornyd made their first contribution in https://github.com/kontent-ai/model-generator-net/pull/155

**Full Changelog**: https://github.com/kontent-ai/model-generator-net/compare/7.0.0...8.0.0-beta.1

## 7.0.0 (2022-04-21)

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
  * split models for Delivery SDK and Management SDK
  * introduce _KontentElementId attribute_
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128
* Exclude Guidelines element in #139
* Include `<auto-generated>` tag in  #147 

### 💥 Breaking changes!
* Generating management models with _KontentElementId_ attribute uses Management SDK
  * Management API key is required from now on
* Parameter _-c (--contentmanagementapi)_ was replaced with _-m (--managementapi)_

### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0

## 7.0.0-beta5 (2022-04-21)  _(prerelease)_

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
* * split models for Delivery SDK and Management SDK
* * introduce KontentElementId attribute
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128
* Exclude Guidelines element in #139


### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0-beta5

## 7.0.0-beta4 (2022-04-07)  _(prerelease)_

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
* * split models for Delivery SDK and Management SDK
* * introduce KontentElementId attribute
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128
* Exclude Guidelines element in #139


### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0-beta4

## 7.0.0-beta3 (2022-04-01)  _(prerelease)_

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
* * split models for Delivery SDK and Management SDK
* * introduce KontentElementId attribute
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128
* Exclude Guidelines element in #139


### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0-beta3

## 7.0.0-beta2 (2022-03-30)  _(prerelease)_

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
* * split models for Delivery SDK and Management SDK
* * introduce KontentElementId attribute
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128
* Exclude Guidelines element in #139


### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0-beta2

## 7.0.0-beta1 (2022-03-10)  _(prerelease)_

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
* * split models for Delivery SDK and Management SDK
* * introduce KontentElementId attribute
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128
* Exclude Guidelines element in #139


### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0-beta1

## 7.0.0-beta0 (2022-03-03)  _(prerelease)_

### What's Changed
* Enable further customization of the CustomTypeProvider  in #111
* New class format in #116
* * split models for Delivery SDK and Management SDK
* * introduce KontentElementId attribute
* Introduce subpages element in #127
* Prepare multitarget setup in #126
* Use [Management SDK](https://github.com/Kentico/kontent-management-sdk-net) in #128

### New Contributors
* @ondrabus made their first contribution in https://github.com/Kentico/kontent-generators-net/pull/119

**Full Changelog**: https://github.com/Kentico/kontent-generators-net/compare/6.0.1...7.0.0-beta0

## 7.0.0-alpha3 (2022-01-13)  _(prerelease)_

Support for subpages, multitarget setup

## 7.0.0-alpha2 (2021-10-01)  _(prerelease)_

First version of generator supporting REST MAPI v2

- fix generation of snippet element IDs in content types with MAPI

## 7.0.0-alpha0 (2021-08-26)  _(prerelease)_

First version of generetor supporting REST MAPI v2

## 7.0.0-alpha1 (2021-08-26)  _(prerelease)_

First version of generator supporting REST MAPI v2

- fix generation of snippets in content types

## 6.0.1 (2020-11-13)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/6.0.1

**Features:**
- Updated to .NET 5
- [Added releases for Linux and macOS](https://github.com/Kentico/kontent-generators-net#standalone-app-for-windows--linux--macos-) #66

## 6.0.0 (2020-10-06)

Compatibility release for [.NET Delivery SDK v14](https://github.com/Kentico/kontent-delivery-sdk-net/releases/tag/14.0.0)

New features / breaking changes:
- SDK types such as `Asset`, `MultipleChoiceOption`, and `TaxonomyGroup` are now generated as respective interfaces (`IAsset`, `IMultipleChoiceOption`, and `ITaxonomyGroup`)
- `generatepartials` is enabled by default

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/6.0.0

## 5.0.1 (2020-04-13)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/5.0.1

Bug fixes:
- [JsonProperty attributes in CM API models were generated incorrectly](https://github.com/Kentico/kontent-generators-net/issues/102)

## 5.0.0 (2020-03-31)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/5.0.0

**New features:**
- all members are sorted alphabetically to make the diffs more readable (based on #99  by @xantari)
- line endings are unified to `Environment.NewLine` (CRLF on Windows) (based on #98  by @xantari)

**Compatibility:**
- [Delivery SDK v13.0.1 and above](https://github.com/Kentico/kontent-delivery-sdk-net/releases/tag/13.0.1)

**Breaking changes:**
 - generated models now contain a low-level assembly called `Kentico.Kontent.Delivery.Abstractions`

## 5.0.0-beta5 (2020-03-29)  _(prerelease)_

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/5.0.0-beta5

**New features:**
- all members are sorted alphabetically to make the diffs more readable (based on #99  by @xantari)
- line endings are unified to `Environment.NewLine` (CRLF on Windows) (based on #98  by @xantari)

## 5.0.0-beta2 (2020-02-14)  _(prerelease)_

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/5.0.0-beta2

**Breaking changes:**
- the utility generates models compatible with the [Delivery SDK v13.0.1-beta1](https://github.com/Kentico/kontent-delivery-sdk-net/releases/tag/13.0.1-beta6) and above. 
  - because the models are now contained within a low-level assembly called `Kentico.Kontent.Delivery.Abstractions`

## 4.1.0 (2019-11-22)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/4.1.0

**New features:**
- advanced configuration via the command-line (https://github.com/Kentico/kontent-generators-net#advanced-configuration-preview-api-secure-api)

**Breaking changes:**
- all switches need to be followed by a boolean value true/false. e.g. `-s=true` (or `-s true` or `--structuredmodel=true`) instead of just `-s`
  - the need to follow the syntax described [here](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.commandlineconfigurationextensions.addcommandline?view=dotnet-plat-ext-3.1)

## 4.0.5 (2019-11-16)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/4.0.5

## 4.0.2 (2019-11-16)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/4.0.2

fixes:
- migrated to netcore3.0
- https://github.com/Kentico/kontent-generators-net/issues/92

## 4.0.1 (2019-09-24)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/4.0.1

## 4.0.0 (2019-09-24)

https://www.nuget.org/packages/Kentico.Kontent.ModelGenerator/4.0.0

Kentico Cloud has been rebranded to Kentico Kontent. More details at: https://kontent.ai/blog/moving-to-caas-with-kentico-kontent

Breaking changes:
- .NET global tool (NuGet) renamed to: Kentico.Kontent.ModelGenerator
- the exe file renamed to: KontentModelGenerator.exe

## 3.0.0 (2019-03-13)

- #87 
- #88 

Thanks to @gerfaut!

This release is compatible with the Delivery SDK for .NET v10+.

https://www.nuget.org/packages/KenticoCloud.ModelGenerator/3.0.0

## 2.1.0 (2019-02-01)

https://www.nuget.org/packages/KenticoCloud.ModelGenerator/2.1.0

- added support for [custom elements](https://github.com/Kentico/cloud-generators-net/pull/85)

## 2.0.3 (2019-01-09)

https://www.nuget.org/packages/KenticoCloud.ModelGenerator/2.0.3

## 1.5.198 (2018-09-13)

- Compatible with [Kentico Cloud Delivery SDK for .NET 6.0.0](https://github.com/Kentico/delivery-sdk-net/releases/tag/6.0.0) and newer

## 1.5.129 (2018-01-24)

Breaking changes in command line parameters:

`-sf` (aka `--filenamesuffix`) -> `-f` 
`-gp` (aka `--generatepartials`) -> `-g`
`-cma` (aka `--contentmanagementapi`) -> `-c`

## Earlier releases

The following versions were published without release notes:

`8.3.0-beta.3`, `8.3.0-beta.2`, `8.3.0-beta.1`, `8.1.1-beta.4`, `8.1.1-beta.3`, `8.1.1-beta.2`, `8.1.1-beta.0`, `6.0.2-beta1`, `1.6.59`, `1.6.58`, `1.6.57`, `1.6.20`, `1.5.226`, `1.5.219`, `1.5.207`, `1.5.186`, `1.5.178`, `1.5.177`, `1.5.174`, `1.5.165`, `1.5.157`, `1.5.152`, `1.5.145`, `1.5.139`, `1.5.135`, `1.5.132`, `1.5.126`, `1.5.122`, `1.5.117`, `1.5.104`, `1.5.101`, `1.5.100`, `1.5.99`, `1.5.98`, `1.5.97`, `1.5.92`, `1.5.89`, `1.5.87`, `1.5.85`, `1.5.82`, `1.5.81`, `1.5.80`, `1.5.79`
