# AGENTS.md

Guidance for coding agents working on the **Model generator**. The root `AGENTS.md` covers everything repo-wide; this file holds only what is specific to this product.

## Overview

A `dotnet tool` (`KontentModelGenerator`, project `Kontent.Ai.ModelGenerator`, `PackAsTool`) over a library (`Kontent.Ai.ModelGenerator.Core`, also shipped as a package). Both ship in lockstep on `<ModelGeneratorVersion>`. It generates strongly typed content models in two modes:

- **Delivery mode** (default) reads the Delivery *types* endpoint through `IDeliveryClient` and emits `public partial record X` with `[ContentTypeCodename]`, a `{Prop}Codename` constant per element and `[JsonPropertyName]` properties. The record is `partial`, not `sealed`, because a same-named partial is the supported extension point. There is no type-provider output any more: `Kontent.Ai.Delivery.SourceGeneration` derives `GeneratedTypeProvider` from the attributes in the consumer's own compilation, so `--withtypeprovider` is gone and stays gone.
- **Management mode** (`-m`/`--management`) lists content types and snippets through `IManagementClient`, inlines snippet elements (`SnippetExpander` — MAPI already delivers snippet element codenames pre-prefixed, so expansion never rewrites a codename) and emits `public sealed partial record X : IElementsModel` with `[ContentType]`/`[ContentElement]` and a sibling `enum` with `[ContentOption]` members per multiple-choice element.

**Code generation is Roslyn `SyntaxFactory`**, formatted through `Formatter` on an `AdhocWorkspace`; no templates. Properties are emitted in ordinal identifier order so the same content model produces the same file on every machine. Element → CLR mapping is one table per mode: `Core/Common/Property.cs` for Delivery, `ManagementElementService` over the `ManagementElementInput` record hierarchy (`Common/ManagementElementInputs.cs`) for Management. An element type either mode does not emit is a warn-and-skip, never a crash and never silence — `CodeGeneratorBase.WriteConsoleErrorMessage` has a default arm for exactly that reason.

**Run flow** is `Program`: `ArgHelpers.FindInvalidArgs` reports *every* problem before anything runs; mode switches are stripped before `AddCommandLine` sees them; `appSettings.json` (optional, from the working directory) is overridden by the command line; the SDK's own options validation is re-run by hand in `ValidationExtensions` because `ValidateOnStart` only fires under a `Host` and this tool builds a bare `ServiceProvider`. Output is flat, `{OutputDir}/{ClassName}.cs`, with a path-traversal guard; type files overwrite, the `--baseRecord` file is kept if present.

## Decided — do not reopen

- **Management models are uniformly nullable.** `null` means "leave this element alone" on upsert, so `--nullability` is a Delivery-only flag and passing it with `-m` is an error with a targeted message.
- **Content-model constraints are not mirrored** on generated models (no `[StringLength]`, no `[Required]`); they are enforced server-side, and `is_required` is a publish gate, not an upsert-shape constraint. Test-pinned.
- **Management models are environment-specific** — they carry element ids, so the docs say "regenerate after cloning".
- **A parameter that belongs to the other mode is an error**, not a silently ignored argument. `appSettings.json` values are not subject to this.
- **Legacy (v18 and earlier) and Extended Delivery models are not supported**; the README points at the `9.0.0` tag of the old repository for them.
- `NullabilityMode.Strict` is the default; the switch to `Semantic` is promised for the next major (XML doc on `CodeGeneratorOptions.Nullability` and the README). Do not flip it in a minor.

## The CLI is the contract

The tool has no public-API approval gate; its arguments are the contract. Mappings live in `CommandLine/ArgMappingsRegister.cs` in three tables (general, Delivery, Management) that are deliberately not unioned — `-i` targets a different options section in each mode. `-p`/`--projectid` are backward-compatibility aliases for the environment id; keep them. Any argument change needs `ArgHelpersTests` coverage, and a behaviour change at the process boundary needs a `ProgramTests` exit-code test. CI smoke-tests the packed tool by installing it and running it with no arguments, asserting on the tool's own `EnvironmentId` validation message — that message is part of the contract too.

`Kontent.Ai.ModelGenerator.Core` *does* have an approval snapshot (`Core.Tests/ApiApproval`). `DeliveryClassCodeGenerator` and `ManagementClassCodeGenerator` are `sealed`; the abstract `ClassCodeGenerator` is the only extension point left.

## Cross-product coupling

This product consumes `Kontent.Ai.Delivery` and `Kontent.Ai.Management` at the declared floors in `Directory.Packages.props`, which by design lag the SDKs in this repository. Consequences that shape the code:

- Emitted Management code names SDK types by hand (`IElementsModel`, `RichTextValue`, `AssetReference`, the `Annotations` attributes, …). `ManagementEmittedTypesMatchSdkTests` checks every such name against the Management SDK's approval snapshot, embedded as a resource because the test project compiles against the older floor. A new SDK type in the emitter means a stub in `SdkStubsSource`, a namespace in `GetApiUsings()`, and an entry in that test's `EmitEveryElementKind`, or the drift gate never sees it.
- `Program.ConfigureDeliveryMode` uses the options-instance overload of `AddDeliveryClient` on purpose: it is the one registration form the published package and the in-repo project share across a Delivery major, so the tool compiles on both CI legs.
- `Kontent.Ai.Delivery.SourceGeneration` is not swapped to a ProjectReference in `/p:UseProjectReferences=true` mode (it is consumed as an analyzer). The Core tests reference it, so a source-generator change shows up here only after it ships.
- A change to the emitted Management shape that needs a matching SDK change ships as a pair. Say so in the changelog ("needs the `Kontent.Ai.Management` release that ships alongside this one") and in the README's version table; the floor is raised in its own PR after that SDK release.

The generated Management model shape (records, mapping attributes, collection types) is agreed jointly with `src/management`, which lists it under its open questions. Change it there and here together, not in one place.

## Testing conventions

- xUnit + AwesomeAssertions + NSubstitute + Verify. No MockHttp: the SDK clients are substituted at the interface (`Substitute.For<IManagementClient>()`), and inputs are built in memory — there is no JSON fixture directory, by decision.
- Four verification layers, and a new element kind touches all of them: (1) Delivery output is compared as trimmed string equality against `Core.Tests/Assets/*.txt`; (2) `AssertCompiledCode` compiles the emitted source into a `CSharpCompilation` — Delivery against the real SDK assemblies plus a hand-written `ContentTypeCodenameAttribute` (the source generator would normally emit it), Management against `SdkStubsSource`; (3) the SDK drift gate above; (4) the Core approval snapshot.
- Management output is asserted on substrings and regexes, not snapshots; `ManagementCodeGeneratorTests` captures the emitted string through the substituted `IOutputProvider`.
- Coverage thresholds are per test project and scoped to the assembly that project owns (both projects reference both assemblies, so an unscoped report is meaningless). Raise a threshold only to a value proven green on both Ubuntu and Windows.
- `Program`-level tests exist because parsing runs before the container exists; message reporting is separated from parsing so tests can run in parallel. Do not write to `Console` from `ArgHelpers`.

## Project structure

- `Kontent.Ai.ModelGenerator/` — `Program.cs`, `CommandLine/` (arg mappings, `ArgHelpers`, validation, SDK version probe), `FileSystemOutputProvider`, `UserMessageLogger`, `appSettings.json` (a JSONC template the README tells users to copy; not installed with the tool).
- `Kontent.Ai.ModelGenerator.Core/` — `CodeGeneratorBase` and the two mode orchestrators, `Generators/` (Roslyn emitters), `Common/` (`ClassDefinition` with the identifier-collision registry, `Property`, the `ManagementElementInput` records, `SnippetExpander`), `Services/` (the Management element metadata adapter and element service).
- `self-contained.ps1` — maintainer convenience for single-file publishes per RID; not wired into any workflow. Releases are NuGet packages only.
- `docs/upgrade/10-to-11.md` — the in-progress major's guide. Its headline promise is that Delivery models are byte-identical to `10.2.0` while Management models must be regenerated; keep both true or update the guide.
