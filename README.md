[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]

[![Discord][discussion-shield]][discussion-url]

# Kontent.ai .NET

> [!WARNING]
> **Work in progress — not yet the home of the shipped SDKs.**
>
> This repository is being assembled by consolidating the Kontent.ai .NET repositories
> into a single monorepo. Products are moving here one at a time, and nothing has been
> published to NuGet from this repository yet.
>
> **Until the migration completes, use the existing repositories and packages:**
>
> | Package | Repository |
> |---|---|
> | `Kontent.Ai.Delivery` and friends | [delivery-sdk-net](https://github.com/kontent-ai/delivery-sdk-net) |
> | `Kontent.Ai.Management` | [management-sdk-net](https://github.com/kontent-ai/management-sdk-net) |
> | `Kontent.Ai.Sync` | [sync-sdk-net](https://github.com/kontent-ai/sync-sdk-net) |
> | `Kontent.Ai.AspNetCore` | [aspnetcore-extensions](https://github.com/kontent-ai/aspnetcore-extensions) |
> | `Kontent.Ai.ModelGenerator` | [model-generator-net](https://github.com/kontent-ai/model-generator-net) |
>
> Packages published from here will be announced when the move is complete. Issues and
> pull requests are welcome, but expect the layout to keep changing until then.

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

## Building

Requires the .NET SDK pinned in [`global.json`](./global.json).

```sh
dotnet build          # everything
dotnet test           # everything, with per-product coverage gates
```

Each product also has its own solution under `src/<product>/` if you only want to open one.

## Releasing

Versions live in [`eng/Versions.props`](./eng/Versions.props), one property per product.
A release is a version bump plus a changelog entry, then a tag:

```sh
dotnet run eng/scripts/update-version.cs -- <product> <major|minor|patch>
# review, commit, merge, then tag <product>-v<version>
```

Publishing is routed by the tag: `aspnetcore-v0.17.0` packs and publishes only the
ASP.NET Core product. The pipeline refuses to publish if the tag disagrees with
`eng/Versions.props`, if the changelog has no entry for that version, or if a package
would depend on a `Kontent.Ai.*` version that is not yet on NuGet.

## Contributing

See [`CONTRIBUTING.md`](./CONTRIBUTING.md) and [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md).

## License

Distributed under the MIT License. See [`LICENSE.md`](./LICENSE.md) for more information.

<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://github.com/kontent-ai/Home/wiki/Checklist-for-publishing-a-new-OS-project#badges-->
[contributors-shield]: https://img.shields.io/github/contributors/kontent-ai/dotnet.svg?style=for-the-badge
[contributors-url]: https://github.com/kontent-ai/dotnet/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/kontent-ai/dotnet.svg?style=for-the-badge
[forks-url]: https://github.com/kontent-ai/dotnet/network/members
[stars-shield]: https://img.shields.io/github/stars/kontent-ai/dotnet.svg?style=for-the-badge
[stars-url]: https://github.com/kontent-ai/dotnet/stargazers
[issues-shield]: https://img.shields.io/github/issues/kontent-ai/dotnet.svg?style=for-the-badge
[issues-url]: https://github.com/kontent-ai/dotnet/issues
[license-shield]: https://img.shields.io/github/license/kontent-ai/dotnet.svg?style=for-the-badge
[license-url]: https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md
[discussion-shield]: https://img.shields.io/discord/821885171984891914?color=%237289DA&label=Kontent%2Eai%20Discord&logo=discord&style=for-the-badge
[discussion-url]: https://discord.com/invite/SKCxwPtevJ
