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
| Sync SDK | `src/sync` | `Kontent.Ai.Sync`, `Kontent.Ai.Sync.Abstractions` |

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

[dotnet-shield]: https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/download/dotnet/8.0
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
