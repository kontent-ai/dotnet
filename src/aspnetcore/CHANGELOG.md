# Kontent.Ai.AspNetCore

All notable changes to this package are documented here.
Entries before the move to this monorepo were imported from the GitHub Releases of
[kontent-ai/aspnetcore-extensions](https://github.com/kontent-ai/aspnetcore-extensions).

## Unreleased

Targets .NET 10, moving from `net8.0` to `net10.0`. Webhook signature verification is hardened in two ways
worth reading before you upgrade, and the public surface is tightened while the package is still pre-1.0:
types are sealed, the webhook payload models become immutable records, and two dependency-injected values
stop being public properties.

### Breaking changes

- **Every public type is `sealed`, and the webhook payload models are `record`s with `init` properties.** `WebhookNotification`, `WebhookModel`, `WebhookData`, `WebhookMessage` and `WebhookItem` describe an inbound payload: nothing mutates one after it is bound, and comparing two by value is more often what you want than comparing by reference. Each keeps its public parameterless constructor, so `System.Text.Json` binds them exactly as before, and property names and JSON attributes are unchanged. Code that constructs one with an object initializer still compiles; code that assigns a property *after* construction does not.
- **`Reference` is removed.** A public model with `ById` / `ByCodename` / `ByExternalId` factories that nothing in the package produced, consumed, or referenced — it was reachable only from its own unit test.
- **`SignatureMiddleware.WebhookOptions` and `AssetTagHelper.ImageTransformationOptions` are no longer public properties.** Both were dependency-injected values exposed for no scenario. The middleware's carried the shared webhook secret. The tag helper's was worse than redundant: Razor binds every public settable property on a tag helper to an HTML attribute unless told otherwise, so `<img-asset>` accepted an `image-transformation-options` attribute that was never meant to exist. Both are now constructor parameters held privately.
- **`UseWebhookSignatureValidator` no longer takes an optional `WebhookOptions`.** Three overloads accepting reference types meant `UseWebhookSignatureValidator(predicate, null)` could not be resolved. "Use the options from the container" is now its own two-argument overload, and the `WebhookOptions` overload takes a required, non-null instance. Calls that passed options, an `Action<WebhookOptions>`, a configuration section, or nothing at all are unaffected.
- **`net8.0` → `net10.0`.** There is no multi-targeting, so a project on .NET 8 cannot install this release at all — restore fails with `NU1202: Package Kontent.Ai.AspNetCore is not compatible with net8.0`. Move to .NET 10 first. This is a pre-1.0 package, so the minor version carries the break.

### Security

- **Webhook signatures are compared in constant time.** The check compared the Base64 signature strings with an ordinary ordinal comparison, which returns as soon as two characters differ. A caller able to time the response could recover the expected signature one character at a time and forge a request. The comparison now runs over the raw HMAC bytes via `CryptographicOperations.FixedTimeEquals`. A signature that is not well-formed Base64, or that does not decode to exactly one HMAC-SHA256 digest, is rejected before any comparison — digest length is fixed, so rejecting on length leaks nothing.
- **A missing webhook secret now fails loudly instead of accepting requests.** With `WebhookOptions.Secret` unset, the middleware hashed the body with an *empty key* and compared against that, so anything signed with the same empty key passed validation — a misconfigured deployment silently accepted forged webhooks, and a correctly-signed one looked like a bad signature. The middleware now throws `InvalidOperationException` naming the missing setting.

  If you relied on running without a secret, set `WebhookOptions.Secret` to the value shown in the webhook's settings in Kontent.ai. There is no configuration in which the previous behaviour was safe.

### Fixed

- **Webhook signature validation honors client disconnection.** Reading the request body now observes `HttpContext.RequestAborted`, so an aborted request stops the read instead of buffering the whole body first.
- **`<img-asset>` reads `width` and `height` invariantly, and no longer throws on values that are not numbers.** The attributes were parsed with `Convert.ToDouble` in the server's culture while `ImageUrlBuilder` writes the value back invariantly, so the round trip disagreed wherever `.` groups digits — on a `de-DE` server `width="1.5"` produced `?w=15`, a silent tenfold resize. The same call threw `FormatException` for any value HTML allows but the image API has no equivalent for (`100%`, `auto`, a CSS `calc`), taking the render down with a 500. Such values now leave the transformation alone; the attribute still renders on the element, so it keeps working as plain HTML.

### Dependencies

No shipped dependency floors changed. This package has no direct `Microsoft.Extensions.*` references — it declares `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, so those assemblies come from the ASP.NET Core shared framework rather than from a package.

## 0.17.0-preview.1 (2026-08-03)

### Changed
- Package metadata and SourceLink now point at the [`kontent-ai/dotnet`](https://github.com/kontent-ai/dotnet)
  monorepo, where this package is now built. Debugging into SDK source resolves there
  rather than to `aspnetcore-extensions`. No API or behaviour change.

## 0.16.1 (2026-04-24)

### What's Changed
#### New features
* Add an `AddKontentRichText(Action<IServiceProvider, IHtmlResolverBuilder>)` overload that exposes the application's `IServiceProvider` to the resolver-builder configurator. This lets rich-text resolvers pull in DI-registered services (URL helpers, custom route resolvers, `IOptions<T>` bindings, feature flags, etc.) at the registration site instead of forcing consumers to drop down to a raw `services.AddSingleton<IHtmlResolver>(sp => ...)` and replicate what `AddKontentRichText` does internally. The existing `Action<IHtmlResolverBuilder>` overload is unchanged and now forwards to the new one, so `RemoveAll<IHtmlResolver>()` behavior applies across both overloads in any order. Mirrors the established .NET convention of a DI-aware overload alongside the simpler callback form (`AddDbContext`, `AddHttpClient`, etc.). Closes [#19](https://github.com/kontent-ai/aspnetcore-extensions/issues/19).

#### Documentation
* Add a README snippet under the `rich-text` section demonstrating the `IServiceProvider`-aware overload.

#### Tests
* Cover the new overload with parity tests: configurator invocation, `IServiceProvider` access, lazy build on first resolution, singleton lifetime, and last-registration-wins across both overloads in either order. Total tests: `56` → `62`.

### New Contributors

**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.16.0...0.16.1

## 0.16.0 (2026-04-22)

### Breaking changes
- aligns with [Kontent.Ai.Delivery 19.0](https://github.com/kontent-ai/delivery-sdk-net/releases/tag/19.0.0). Consumers must update `Kontent.Ai.Delivery` and `Kontent.Ai.Urls` to `19.0` or later.
- `img-asset` tag helper's `asset` property now accepts `IAsset` (the previous `IImage` interface was removed in Delivery SDK 19.0). No view changes are required for typical usage; pass `IAsset` values returned by element accessors and rich-text assets.
- the `src` fallback attribute emitted by `img-asset` is now deterministic: it uses the largest width in `ResponsiveWidths` rather than whichever width was iterated last. Consumers who declared `ResponsiveWidths` in ascending order (including the sample in the README) will see identical output; consumers who declared them unsorted or descending will see a higher-resolution fallback image.
- `Nullable` reference types are now enabled on the public API. Most users will see no change, but callers passing `null` into parameters that previously tolerated it may see new warnings.

### What's Changed
#### New features
* Expose additional image transformation attributes on `<img-asset>`: `format`, `quality`, `fit`, `auto-format`, `compression`. These apply to every URL the tag helper generates (both `src` and each `srcset` entry).
* Add a `rendition` attribute on `<img-asset>` that applies an asset rendition defined by content editors. When a rendition is applied, its crop is used as `src`, `srcset`/`sizes` are skipped (a rendition is a single crop), and only encoding-level transforms layer on top. Currently Kontent.ai exposes a single `default` rendition.
* Add a `<rich-text>` tag helper and a `ToHtmlContentAsync` extension method for rendering Kontent.ai structured rich-text content in Razor views. Both integrate with the Delivery SDK's `IHtmlResolver`, pick up a DI-registered resolver automatically, and fall back to SDK built-in defaults when none is configured. A `services.AddKontentRichText(...)` extension provides one-line DI registration with an optional resolver-builder configurator.

#### Modernization
* Adopt centralized package management, introduce `Directory.Build.props` / `Directory.Packages.props` / `global.json` to mirror `delivery-sdk-net`.
* Bump `Kontent.Ai.Delivery.Abstractions` and `Kontent.Ai.Urls` to `19.0.0`, and add a direct reference to `Kontent.Ai.Delivery 19.0.0` (required by the new `<rich-text>` tag helper; installing `Kontent.Ai.AspNetCore` now brings in the full Delivery SDK).
* Drop the redundant `Microsoft.AspNetCore.Http.Abstractions 2.2.0` package reference (covered by `Microsoft.AspNetCore.App` framework reference) and bump `Microsoft.Extensions.Options` to `8.0.2`.
* Enable `Nullable`, `ImplicitUsings`, .NET analyzers, and deterministic builds across the solution.
* Bump test dependencies: `xunit 2.4.1` → `2.9.3`, `Microsoft.NET.Test.Sdk 17.2.0` → `17.14.1`, `Microsoft.AspNetCore.TestHost 6.0.7` → `8.0.11`, `coverlet` → `3.2.0`.
* Rewrite CI workflows: Ubuntu + Windows build matrix, `global-json-file` SDK pinning, pack-validation job, OIDC-based NuGet publishing via `NuGet/login`, `gh release upload` for release artifacts, and a dedicated `codeql-analysis` workflow.

#### Documentation
* Port the README examples to .NET 8+ minimal hosting (`WebApplication.CreateBuilder`).
* Add a complete reference table for every `<img-asset>` attribute, including the new ones introduced in this release.
* Add a **Renditions** section describing how rendition URLs are constructed and what currently exists in the Delivery API.
* Add an **Attribute interaction matrix** that spells out which attributes apply, which are ignored, and whether `srcset`/`sizes` are generated, for each combination of `rendition` + other attributes.
* Add a **Custom asset domain** section clarifying that the feature is wired up on the Delivery SDK side (`WithCustomAssetDomain`) and that the tag helper consumes the rewritten `IAsset.Url` as-is.
* Add an **Upgrading to v19** section describing the `IImage` → `IAsset` migration for consumers coming from 0.15.x.
* Add a **`rich-text` tag helper** section covering DI registration via `AddKontentRichText`, per-view resolver override, the `ToHtmlContentAsync` extension alternative, and the zero-config fallback to SDK built-in resolvers.

#### Fixes
* Fix a bug in `AssetTagHelper` where the per-tag `ResponsiveWidths` attribute was ignored during `srcset` generation in favor of the globally configured widths.
* Fix a bug in `AssetTagHelper` where the `src` fallback attribute depended on the iteration order of `ResponsiveWidths`.
* Rename `IApplicationBuilderExtensions.cs` to match its actual class name.

#### Tests
* Cover `ImageTransformation` helpers with tests (closes [#10](https://github.com/kontent-ai/aspnetcore-extensions/issues/10)) and expand coverage to `SignatureMiddleware` happy path + body-preservation, all three `UseWebhookSignatureValidator` overloads, `Reference` factory methods, the new `<img-asset>` attributes, the `<rich-text>` tag helper, the `ToHtmlContentAsync` extension, and `AddKontentRichText` DI registration. Total tests: `4` → `56`.

### New Contributors

**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.15.0...0.16.0


### What's Changed
* Modernize, improve imghelpers, add rich text helpers by @pokornyd in https://github.com/kontent-ai/aspnetcore-extensions/pull/18


**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.15.0...0.16.0

## 0.15.0 (2025-11-14)

### Breaking changes
- legacy webhooks are no longer supported in the product and were therefore removed from this project altogether. More info on current webhooks and their use can be found in [the related Kontent.ai docs](https://kontent.ai/learn/docs/webhooks/webhooks/net).
- the tool now targets .NET 8 

### What's Changed
* EN-628 remove legacy webhooks by @sevcik-martin in https://github.com/kontent-ai/aspnetcore-extensions/pull/17

### New Contributors
* @sevcik-martin made their first contribution in https://github.com/kontent-ai/aspnetcore-extensions/pull/17

**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.14.1...0.15.0

## 0.14.1 (2024-05-13)

### What's Changed
* Update webhook validation by @pokornyd in https://github.com/kontent-ai/aspnetcore-extensions/pull/16


**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.14.0...0.14.1

## 0.14.0 (2024-04-04)

### Features
>[!WARNING]
>Breaking change

- Support for updated webhook functionality, see [webhook documentation](https://kontent.ai/learn/docs/webhooks/webhooks/net) for more information.
  - Legacy webhooks (deprecated, to be removed from the product later) moved to `Kontent.Ai.AspNetCore.Webhooks.Models.Legacy` namespace


### What's Changed
* Added implementation of new webhook type by @EduardDumitru in https://github.com/kontent-ai/aspnetcore-extensions/pull/14

### New Contributors
* @EduardDumitru made their first contribution in https://github.com/kontent-ai/aspnetcore-extensions/pull/14

**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.13.1...0.14.0

## 0.13.1 (2022-08-03)

### What's Changed
* Use the latest version of `Kontent.Ai.Urls` and `Kontent.Ai.Delivery.Abstractions` package


**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.13.0...0.13.1

## 0.13.0 (2022-08-03)

### What's Changed
* **💥 Breaking change!** Migration to Kontent.ai by @pokornyd in https://github.com/kontent-ai/aspnetcore-extensions/pull/12
    * Changing package name from Kentico.Kontent.AspNetCore to Kontent.Ai.AspNetCore
    * Changing Kentico.Kontent.* namespaces to Kontent.Ai.*
* 💥 Breaking change! Target only .NET 6 


**Full Changelog**: https://github.com/kontent-ai/aspnetcore-extensions/compare/0.12.0...0.13.0

## 0.13.0-beta.1 (2022-08-02)  _(prerelease)_

### What's Changed
* Migration by @pokornyd in https://github.com/kontent-ai/aspnetcore/pull/12


**Full Changelog**: https://github.com/kontent-ai/aspnetcore/compare/0.12.0...0.13.0-beta.1

## 0.12.0 (2022-04-29)

### Summary
* `WebhookModel` was removed in favor of `ManagementWebhookModel` and `DeliveryWebhookModel`, to be used with management and (preview) delivery webhook triggers respectively. For further information on webhook types, see [webhooks reference documentation](https://kontent.ai/learn/reference/webhooks-reference/).

### What's Changed
* Wrong namespace by @petrsvihlik in https://github.com/Kentico/kontent-aspnetcore/pull/4
* Temporal fix of release by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/5
* Upgrade to latest packages by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/6
* Upgrade target to .NET 6 by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/7
* 8 cannot deserialize webhookmodel by @pokornyd in https://github.com/Kentico/kontent-aspnetcore/pull/9
* VNext release by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/11

### New Contributors
* @Simply007 made their first contribution in https://github.com/Kentico/kontent-aspnetcore/pull/5
* @pokornyd made their first contribution in https://github.com/Kentico/kontent-aspnetcore/pull/9

**Full Changelog**: https://github.com/Kentico/kontent-aspnetcore/compare/v0.11.1...0.12.0

## 0.12.0-beta4 (2022-04-28)  _(prerelease)_

### What's Changed
* 8 cannot deserialize webhookmodel by @pokornyd in https://github.com/Kentico/kontent-aspnetcore/pull/9
* VNext release by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/11

### New Contributors
* @pokornyd made their first contribution in https://github.com/Kentico/kontent-aspnetcore/pull/9

**Full Changelog**: https://github.com/Kentico/kontent-aspnetcore/compare/0.12.0-beta3...0.12.0-beta4

## 0.12.0-beta3 (2021-11-25)  _(prerelease)_

### What's Changed
* Wrong namespace by @petrsvihlik in https://github.com/Kentico/kontent-aspnetcore/pull/4
* Temporal fix of release by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/5
* Upgrade to latest packages by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/6
* Upgrade target to .NET 6 by @Simply007 in https://github.com/Kentico/kontent-aspnetcore/pull/7

### New Contributors
* @Simply007 made their first contribution in https://github.com/Kentico/kontent-aspnetcore/pull/5

**Full Changelog**: https://github.com/Kentico/kontent-aspnetcore/compare/v0.11.1...0.12.0-beta3

## 0.12.0-beta2 (2021-11-12)  _(prerelease)_

Fix release pipeline

## 0.12.0-beta1 (2021-11-12)  _(prerelease)_

Changed the target to .NET 6 - #7

## 0.11.2-beta1 (2021-11-12)  _(prerelease)_

Fix references targetting .NET 5 - https://github.com/Kentico/kontent-delivery-sdk-net/issues/284

## 0.11.1 (2021-02-28)

**Enhancements:**
- Enabled sourcelink

## 0.10.8 (2021-02-27)

## 0.0.9 (2020-08-17)

https://www.nuget.org/packages/Kentico.Kontent.AspNetCore/0.0.9
