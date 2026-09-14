<img src=".github/assets/kontent-ai-dotnet.png" alt="Kontent.ai and .NET" width="480">

# Kontent.ai .NET

[![.NET][dotnet-shield]][dotnet-url]
[![codecov][codecov-shield]][codecov-url]
[![MIT License][license-shield]][license-url]

Official .NET SDKs and tools for Kontent.ai. Choose a product below for installation,
examples, and guides. Each product has its own version and release cadence.

| Product | Use it to | Latest stable release |
|---|---|---|
| [Delivery SDK](./src/delivery/README.md) | Read published content into a .NET app | [![Kontent.Ai.Delivery][delivery-nuget-shield]][delivery-nuget-url] |
| [Management SDK](./src/management/README.md) | Create, update and publish content | [![Kontent.Ai.Management][management-nuget-shield]][management-nuget-url] |
| [Sync SDK](./src/sync/README.md) | Process content changes since the last sync | [![Kontent.Ai.Sync][sync-nuget-shield]][sync-nuget-url] |
| [ASP.NET Core extensions](./src/aspnetcore/README.md) | Render content and receive webhooks in ASP.NET Core | [![Kontent.Ai.AspNetCore][aspnetcore-nuget-shield]][aspnetcore-nuget-url] |
| [Model generator](./src/model-generator/README.md) | Generate typed C# records from your content model | [![Kontent.Ai.ModelGenerator][model-generator-nuget-shield]][model-generator-nuget-url] |

The badges show published package versions. Documentation describes the code on the branch
you are viewing.

## Building

Requires the .NET SDK pinned in [`global.json`](./global.json). From the repository root:

```sh
dotnet build
dotnet test
```

See [CONTRIBUTING](./CONTRIBUTING.md#working-in-this-repository) for building individual products,
using sibling products from source, and collecting test coverage.

## Layout

Each product lives under `src/<product>/`. Shared infrastructure lives in:

- [`src/common/`](./src/common/README.md) — source compiled into the SDKs.
- [`src/testing/`](./src/testing/README.md) — shared test infrastructure.
- `eng/` — version definitions and build/release scripts.

## Releasing

Maintainers: see the [release guide](./RELEASING.md) for preparing, publishing, and
recovering releases.

## Contributing

See [CONTRIBUTING](./CONTRIBUTING.md) and the [Code of Conduct](./CODE_OF_CONDUCT.md).

## License

Distributed under the MIT License. See [LICENSE](./LICENSE.md).

[dotnet-shield]: https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/download/dotnet/10.0
[codecov-shield]: https://img.shields.io/codecov/c/github/kontent-ai/dotnet?style=for-the-badge
[codecov-url]: https://codecov.io/gh/kontent-ai/dotnet
[license-shield]: https://img.shields.io/github/license/kontent-ai/dotnet.svg?style=for-the-badge
[license-url]: https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md

[aspnetcore-nuget-shield]: https://img.shields.io/nuget/v/Kontent.Ai.AspNetCore?style=for-the-badge
[aspnetcore-nuget-url]: https://www.nuget.org/packages/Kontent.Ai.AspNetCore
[delivery-nuget-shield]: https://img.shields.io/nuget/v/Kontent.Ai.Delivery?style=for-the-badge
[delivery-nuget-url]: https://www.nuget.org/packages/Kontent.Ai.Delivery
[management-nuget-shield]: https://img.shields.io/nuget/v/Kontent.Ai.Management?style=for-the-badge
[management-nuget-url]: https://www.nuget.org/packages/Kontent.Ai.Management
[model-generator-nuget-shield]: https://img.shields.io/nuget/v/Kontent.Ai.ModelGenerator?style=for-the-badge
[model-generator-nuget-url]: https://www.nuget.org/packages/Kontent.Ai.ModelGenerator
[sync-nuget-shield]: https://img.shields.io/nuget/v/Kontent.Ai.Sync?style=for-the-badge
[sync-nuget-url]: https://www.nuget.org/packages/Kontent.Ai.Sync
