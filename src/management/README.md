# Kontent.ai Management SDK for .NET

[![Stable](https://img.shields.io/nuget/v/Kontent.Ai.Management?style=for-the-badge&label=stable)](https://www.nuget.org/packages/Kontent.Ai.Management)
[![Latest](https://img.shields.io/nuget/vpre/Kontent.Ai.Management?style=for-the-badge&label=latest)](https://www.nuget.org/packages/Kontent.Ai.Management/absoluteLatest)
[![Downloads](https://img.shields.io/nuget/dt/Kontent.Ai.Management?style=for-the-badge)](https://www.nuget.org/packages/Kontent.Ai.Management)

The official .NET SDK for the [Kontent.ai Management API](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/) —
programmatic read/write access to your environments: content items, language variants, content models,
assets, taxonomies, workflows and administration.

Use this SDK to **author and manage** content. To serve published content to an application, use the
[Delivery SDK](https://github.com/kontent-ai/dotnet/tree/main/src/delivery) instead.

## Guides

| Guide | What it answers |
|---|---|
| **[Configuration](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/configuration.md)** | Register a client, bind options, name several, tune retries and timeouts |
| **[Requests and results](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/requests-and-results.md)** | Handle failures, choose an identifier, page through large listings |
| **[Content items and variants](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/content-items-and-variants.md)** | Write and publish language variants, move them through a workflow |
| **[Models and rich text](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/models.md)** | Use generated content-type records, author rich text and inline components |
| **[Assets](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/assets.md)** | Upload files, create assets, organize them |
| **[Content model](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/content-model.md)** | Change content types, snippets and taxonomies |
| **[Administration](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/administration.md)** | Languages, workflows, spaces, webhooks, subscription-scoped endpoints |
| **[Upgrade guides](https://github.com/kontent-ai/dotnet/tree/main/src/management/docs/upgrade)** | One per major: [8 → 9](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/upgrade/8-to-9.md) |

## Installation

```bash
dotnet add package Kontent.Ai.Management
```

Targets `net10.0`. See the [changelog](https://github.com/kontent-ai/dotnet/blob/main/src/management/CHANGELOG.md)
for what each release changed.

Coming from **8.x**, read [8 → 9](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/upgrade/8-to-9.md)
first — the API changed shape throughout.

## Prerequisites

- The Management API must be **activated** for your environment.
- A **Management API key**, and your **environment ID** — both from *Environment settings → API keys* in
  Kontent.ai. See [Making requests](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/#section/Making-requests).
- Subscription-scoped endpoints need a different key — see
  [subscription-scoped operations](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/administration.md#subscription-scoped-operations).

## Quick Start

A standalone client, which suits a script or a simple app. Replace `on_roasts` with a content item
codename that exists in your environment.

```csharp
using Kontent.Ai.Management;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Models.Shared;

await using var client = new ManagementClient(new ManagementOptions
{
    EnvironmentId = "<YOUR_ENVIRONMENT_ID>",
    ApiKey = "<YOUR_API_KEY>"
});

var result = await client.GetContentItemAsync(Reference.ByCodename("on_roasts"));

if (result.IsSuccess)
{
    Console.WriteLine(result.Value.Name);
}
else
{
    Console.WriteLine($"{result.StatusCode}: {result.Error?.Message}");
}
```

That returns the item's **metadata**. Element values live in its language variants — see
[content items and variants](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/content-items-and-variants.md).

## Use in an application

Register the client and inject `IManagementClient`:

```csharp
services.AddManagementClient(management => management.Options.Configure(options =>
{
    options.EnvironmentId = "<YOUR_ENVIRONMENT_ID>";
    options.ApiKey = "<YOUR_API_KEY>";
}));
```

Binding from configuration, several named clients, and transport customization are in
[configuration](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/configuration.md).

## Essential behavior

- **Failures come back as results, not exceptions.** `IsSuccess` is the check; a `try`/`catch` will not
  fire. Cancellation and usage errors do throw —
  [details](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/requests-and-results.md#results-and-failures).
- **The container owns a registered client.** Dispose only one you built yourself —
  [details](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/configuration.md#client-registration-and-lifetime).
- **`ListXAsync` fetches and buffers every page** before returning, and is all-or-nothing. Use the
  `ListXPageAsync` overload for large sets —
  [details](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/requests-and-results.md#pagination).
- **Retries depend on the HTTP method.** Every method retries on `429`; other transient failures retry
  only `GET`, `HEAD`, `OPTIONS`, `PUT` and `DELETE`, so a `POST` or `PATCH` that fails mid-flight is
  never replayed —
  [details](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/configuration.md#retries-and-timeouts).
- **A multi-call helper is not a transaction.** `CreateContentItemWithVariantAsync` can leave an item
  with no variant —
  [details](https://github.com/kontent-ai/dotnet/blob/main/src/management/docs/content-items-and-variants.md#create-an-item-with-its-first-variant).

## Contributing

See the [contributing](https://github.com/kontent-ai/dotnet/blob/main/CONTRIBUTING.md) page for where to
file issues, start discussions, and begin contributing.

## License

Distributed under the MIT License — see [`LICENSE.md`](https://github.com/kontent-ai/dotnet/blob/main/LICENSE.md) for details.
