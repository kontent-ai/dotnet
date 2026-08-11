# CLAUDE.md

Guidance for Claude Code agents working anywhere in this repository.

## What this repository is

The Kontent.ai **.NET monorepo** — every official Kontent.ai .NET tool lives here, merged from formerly separate repositories. Each product keeps its own identity: its own version, changelog, README, docs, and release cadence. The monorepo buys shared infrastructure and atomic cross-product changes; it does **not** merge the products into one package or one version.

| Product | Path | Packages |
|---|---|---|
| Delivery SDK | `src/delivery` | `Kontent.Ai.Delivery` + Abstractions, Caching, SourceGeneration, Urls |
| Management SDK | `src/management` | `Kontent.Ai.Management` |
| Sync SDK | `src/sync` | `Kontent.Ai.Sync` + Abstractions |
| ASP.NET Core extensions | `src/aspnetcore` | `Kontent.Ai.AspNetCore` (depends on Delivery) |
| Model generator | `src/model-generator` | `Kontent.Ai.ModelGenerator` + Core (depends on Delivery and Management) |

`eng/products.json` is the machine-readable manifest of this table — version property, changelog path, projects, expected packages, and cross-product dependencies per product. Release tooling reads it; keep it in step with reality.

Products with their own `CLAUDE.md` (e.g. `src/management/CLAUDE.md`) carry product-specific conventions — read it before working there. For any product, its sibling products are canonical references: when a style or architecture question comes up, read how a sibling solved it first, and diverge only with a stated reason.

## Shared source, not shared packages

- **`src/common/`** — infrastructure the SDKs each need an identical copy of (retry predicates, tracking headers, Refit response reading). It is **not a product and not a package**: files are compiled *into* each SDK assembly as `internal` types via `<Compile Include="$(KontentCommonPath)...">`, the way `src/libraries/Common` works in dotnet/runtime. Nothing there may be `public` or reference product types; inclusion is per-file and opt-in. `src/common/README.md` has the full rules and the reasoning for source over a `Kontent.Ai.Core` package — read it before adding or changing anything there.
- **`src/testing/`** — the test-only counterpart (public-API approval printer, shared test infra). Nothing under it ships. Same rule: read its README before adding a copy of something.

## Target framework, language, and style

- **`net10.0`, single-target**, repo-wide — every product moved together; do not reintroduce older targets. SDK pinned via `global.json`.
- `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`, analyzers, deterministic builds — all centralized in the **root** `Directory.Build.props`. Each `src/<product>/Directory.Build.props` must import the root file explicitly (MSBuild stops at the first `Directory.Build.props` it finds — the import is load-bearing).
- **Use modern C# actively**: primary constructors, file-scoped namespaces, `sealed` by default, `record` for DTOs, `required` members, collection expressions, `init`-only setters, pattern matching over `if`/cast chains, `ArgumentNullException.ThrowIfNull`.
- **Lean functional over pure OOP**: immutable records, expression-bodied and pure functions, LINQ pipelines over mutating loops, composition over inheritance hierarchies. No speculative abstraction layers or extensibility hooks nobody asked for.
- **KISS.** The simplest thing that works wins. A clever solution a future reader has to decode loses to a plain one.
- **Dates split by direction, deliberately.** A timestamp the API *sends* is `DateTime` — every Kontent.ai API emits UTC with a `Z`, so it deserializes to `Kind=Utc` and there is no ambiguity to resolve. A timestamp the caller *supplies* is `DateTimeOffset`, because a bare `DateTime` would be interpreted against whatever zone the code happens to run in. `DeliveryClient`, `ManagementClient`, `SyncClient` and the ASP.NET Core webhook models all follow this, and Delivery's filter API takes `DateTime` so read values pass straight into it.

  `DateTimeOffset` is an instant plus a *fixed offset*, not a time zone — it cannot represent `Europe/Prague` or DST. Where the API sends a zone it does so separately (`display_timezone`, an IANA name), and rendering local time means applying `TimeZoneInfo` to the UTC instant either way. So converting reads to `DateTimeOffset` buys a constant `+00:00` and nothing else. This has been raised more than once; it is decided.
- Avoid reflection unless genuinely necessary; cache anything reflective.

## Commenting standards

- **Good code is self-explanatory — do not overcomment.** XML docs belong on public consumer-facing surface (IntelliSense); private implementation does not need narration.
- **Only comment the non-obvious**: a hidden API constraint, a subtle invariant, a server-side quirk workaround, a decision whose rationale would surprise. If removing the comment would confuse nobody, don't write it.
- Do not annotate corrections or history in code — no "changed from X" comments; that belongs in the commit message at most.
- No dead code, no drifting `// TODO`s, no commented-out blocks, no references to dev-only notes or planning files from committed code.
- Do not add defensive code for impossible scenarios. Validate at external boundaries only (public API entry points, deserialized network payloads).

## Build system

- **Root `Directory.Build.props`** — repo-wide build settings, packaging metadata, `$(KontentCommonPath)`/`$(KontentTestingPath)`.
- **`Directory.Packages.props`** — Central Package Management for the whole repo. The `Kontent.Ai.*` entries are **declared dependency floors**, not mere pins: the version there lands in the published nuspec as the minimum a consumer may resolve. A floor must already be on nuget.org, so floors are raised in their own PR *after* the dependency ships — never in the batch that bumps it. The file's own header has the full reasoning.
- **`Directory.Build.targets`** — dual build modes. Default (`dotnet build`) compiles cross-product references against **published packages** at their declared floors — the relationship we actually ship. `/p:UseProjectReferences=true` flips them to in-repo ProjectReferences — the early-warning leg that surfaces sibling breaking changes in the PR that makes them. CI runs both legs, both blocking. Packing in source mode is refused by a structural guard (`KONTENT001`) because it would stamp sibling versions from `eng/Versions.props` instead of the declared floors.
- Build and test from anywhere with plain `dotnet build` / `dotnet test` — prefer commands without explicit paths.

## Versioning and releases — GitHub Actions are the source of truth

**`eng/Versions.props`** holds one version property per product (e.g. `<ManagementVersion>`); each `src/<product>/Directory.Build.props` applies its own. This file — not tags, not changelogs — is what the machinery trusts. The workflows under `.github/workflows/` define the process; when in doubt about how releasing works, read them rather than guessing:

- **`ci.yml`** — build + test on every PR and push to `main`, Ubuntu and Windows, both reference modes.
- **`prepare-release.yml`** (manual dispatch) — pick a bump per product (or an explicit version); it runs `eng/scripts/update-version.cs` to bump `eng/Versions.props` and promote each product's `CHANGELOG.md` `## Unreleased` section, then opens **one PR for the whole batch**. It publishes nothing.
- **`publish-batch.yml`** (manual dispatch, after merging that PR) — creates one GitHub Release per product whose declared version is missing from nuget.org, **in dependency order**, waiting for each to reach NuGet before the next. Defaults to a dry run; re-running skips what's already published. It decides nothing — the state of `eng/Versions.props` vs nuget.org does.
- **`release.yml`** — fires when a GitHub Release is published. The tag routes the release: `<product>-v<version>` (e.g. `management-v9.0.0`) packs and publishes only that product. It **refuses to publish** if the tag disagrees with `eng/Versions.props`, if the changelog has no entry, or if a cross-product `Kontent.Ai.*` dependency is not yet on nuget.org (same-release siblings exempt).
- **`dependency-floors.yml`** (scheduled, monthly) — reports how far the cross-product floors lag nuget.org. Deliberately not a CI gate: a floor is *meant* to lag; raise it only when the consuming code needs the newer API.

So the full release flow is: merge feature PRs (each user-visible change adds to the product's `CHANGELOG.md` under `## Unreleased`) → run *Prepare release* → review and merge its PR → run *Publish batch*. Releases stay independent — a product can be published or abandoned without affecting the others.

## Commits and PRs

- Commit messages: `TICKET-ID - Description` when a ticket exists (e.g. `EN-713 - Add component_types filter`); otherwise a concise lowercase summary matching branch history. Branch names: `TICKET-ID_Short_description`.
- Keep each PR scoped: infra separate from per-product work; version bumps come only from the prepare-release workflow; floor raises in their own PR.
- Public API surface is gated per product by approval snapshots (Verify, printer shared from `src/testing`). Review a `.received.txt` diff line by line before accepting it — only for intended changes. Every shipped package has a gate except `Kontent.Ai.ModelGenerator`, which is `PackAsTool`: its contract is the command line, not a managed surface nobody references. Its arguments are covered by `ArgHelpers`/`Program` tests instead.

## Collaboration stance

- **Be critical, not compliant.** If a request conflicts with these principles, a product's conventions, or good design, push back with reasoning before implementing.
- **No flattery.** Acknowledge, analyze, recommend.
- **Surface trade-offs the user may not have weighed** — public API breakage, sibling divergence, cross-product coupling, wire-contract risk, dependency-floor implications.
- When uncertain about a convention, read the sibling product (or the relevant workflow/README) before asking or assuming.
