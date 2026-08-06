[![.NET][dotnet-shield]][dotnet-url]
[![Build & Test][build-shield]][build-url]
[![codecov][codecov-shield]][codecov-url]
[![Contributors][contributors-shield]][contributors-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]

# Kontent.ai .NET

> [!IMPORTANT]
> **This is the home of the Kontent.ai .NET SDKs.** All five products have moved here with
> their full history, and packages are published from this repository.
>
> The former per-product repositories are **frozen** — they are kept for reference and for
> the release history of versions published before the move. Open issues and pull requests
> here instead:
>
> | Former repository | Now at |
> |---|---|
> | [delivery-sdk-net](https://github.com/kontent-ai/delivery-sdk-net) | [`src/delivery`](./src/delivery) |
> | [management-sdk-net](https://github.com/kontent-ai/management-sdk-net) | [`src/management`](./src/management) |
> | [sync-sdk-net](https://github.com/kontent-ai/sync-sdk-net) | [`src/sync`](./src/sync) |
> | [aspnetcore-extensions](https://github.com/kontent-ai/aspnetcore-extensions) | [`src/aspnetcore`](./src/aspnetcore) |
> | [model-generator-net](https://github.com/kontent-ai/model-generator-net) | [`src/model-generator`](./src/model-generator) |
>
> Package IDs and public APIs are unchanged by the move.

## About

A monorepo for the Kontent.ai .NET SDKs and tooling — one place for the client libraries,
the model generator, integrations and samples, so that a change touching several of them
is one pull request rather than a coordinated release across five repositories.

## Layout

```
src/<product>/     each product, with its own CHANGELOG.md and package metadata
eng/               version source of truth, release routing, build scripts
.github/workflows/ CI and the tag-routed release pipeline
```

Currently migrated:

| Product | Path | Packages |
|---|---|---|
| ASP.NET Core extensions | `src/aspnetcore` | `Kontent.Ai.AspNetCore` |
| Delivery SDK | `src/delivery` | `Kontent.Ai.Delivery`, `Kontent.Ai.Delivery.Abstractions`, `Kontent.Ai.Delivery.Caching`, `Kontent.Ai.Delivery.SourceGeneration`, `Kontent.Ai.Urls` |
| Management SDK | `src/management` | `Kontent.Ai.Management` |
| Model generator | `src/model-generator` | `Kontent.Ai.ModelGenerator`, `Kontent.Ai.ModelGenerator.Core` |
| Sync SDK | `src/sync` | `Kontent.Ai.Sync` |

## Building

Requires the .NET SDK pinned in [`global.json`](./global.json).

```sh
dotnet build          # everything, against published sibling packages
dotnet test           # everything

# Coverage is opt-in. CI adds these, and the per-product thresholds fail the run below them.
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

Each product also has its own solution under `src/<product>/` if you only want to open one.

Products consume each other as `PackageReference`, so the default build is what ships.
`-p:UseProjectReferences=true` swaps those for the source in this tree and answers the other
question — whether the five products at this commit work together. CI runs both and both
block the merge. See [`CONTRIBUTING.md`](./CONTRIBUTING.md#two-build-modes).

## Releasing

Versions live in [`eng/Versions.props`](./eng/Versions.props), one property per product.
A release is a version bump plus a changelog entry, then a tag.

**Actions → Prepare release** is the normal route. Each product has its own dropdown
defaulting to `none`, so one run can bump several products at once and opens a single PR
covering the batch.

After merging that PR, **Actions → Publish batch** creates a GitHub Release for every product
whose declared version is not yet on NuGet — in dependency order, waiting for each to publish
before starting the next. Release notes come from each product's changelog. It defaults to a
dry run, so you can see the plan and the notes before anything is created.

Nothing about that is required: a release is just a GitHub Release tagged
`<product>-v<version>`, so creating them by hand works exactly the same. Releases stay
independent either way — any one can be published or dropped without affecting the rest.

The same bump can be done locally if you prefer:

```sh
dotnet run eng/scripts/update-version.cs -- <product> <prerelease|release|patch|minor|major>
# review, commit, merge, then tag <product>-v<version>
```

Publishing is routed by the tag: `aspnetcore-v0.17.0` packs and publishes only the
ASP.NET Core product. The pipeline refuses to publish if the tag disagrees with
`eng/Versions.props`, if the changelog has no entry for that version, or if a package
would depend on a `Kontent.Ai.*` version that is not yet on NuGet. Packages belonging to
the same product are exempt from that last check — they are published together.

### Cross-product dependency floors

Releasing a product does **not** update the version its siblings depend on. Those floors live
in [`Directory.Packages.props`](./Directory.Packages.props), and raising one is a third step,
in its own PR, after the dependency is on NuGet — doing it sooner makes the repo
unrestorable, including the release that would have published that version.

A floor is the minimum a published package promises to work with, so it is meant to lag
behind the newest sibling. Raise it only when the consuming code actually needs the newer
API. [`CONTRIBUTING.md`](./CONTRIBUTING.md#changing-an-api-that-another-product-consumes) has
the full sequence.

To see where the floors stand:

```sh
dotnet run eng/scripts/dependency-floors.cs
```

Every *Prepare release* PR carries the same report in its body, and **Actions → Dependency
floors** runs it monthly. It only fails if a floor names a version that is not on NuGet at
all — being behind is reported, never enforced.

### Prepared but not released

Preparing and publishing are separate steps, so a batch can bump several products and then
only some get released. To see where each product stands:

```sh
dotnet run eng/scripts/release-status.cs
```

A `PREPARED, NOT PUBLISHED` product is a normal intermediate state, not a problem. Resolve
it whichever way matches your intent:

- **Releasing it later** — do nothing. The version property and changelog entry are already
  valid; tag `<product>-v<version>` whenever you are ready.
- **Abandoning it** — undo the preparation: restore the version in `eng/Versions.props`, and
  delete the `## <version> (<date>)` heading so its notes sit under `## Unreleased` again.

The one thing to avoid is preparing the same product *again* while it is in this state — the
bump would move on from a version that was never published, silently skipping it.

## Contributing

See [`CONTRIBUTING.md`](./CONTRIBUTING.md) and [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md).

## License

Distributed under the MIT License. See [`LICENSE.md`](./LICENSE.md) for more information.

<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://github.com/kontent-ai/Home/wiki/Checklist-for-publishing-a-new-OS-project#badges-->

[dotnet-shield]: https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/download/dotnet/10.0
[build-shield]: https://img.shields.io/github/actions/workflow/status/kontent-ai/dotnet/ci.yml?style=for-the-badge&label=Build%20%26%20Test
[build-url]: https://github.com/kontent-ai/dotnet/actions/workflows/ci.yml
[codecov-shield]: https://img.shields.io/codecov/c/github/kontent-ai/dotnet?style=for-the-badge
[codecov-url]: https://codecov.io/gh/kontent-ai/dotnet
[contributors-shield]: https://img.shields.io/github/contributors/kontent-ai/dotnet.svg?style=for-the-badge
[contributors-url]: https://github.com/kontent-ai/dotnet/graphs/contributors
[issues-shield]: https://img.shields.io/github/issues/kontent-ai/dotnet.svg?style=for-the-badge
[issues-url]: https://github.com/kontent-ai/dotnet/issues
[license-shield]: https://img.shields.io/github/license/kontent-ai/dotnet.svg?style=for-the-badge
[license-url]: https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md
