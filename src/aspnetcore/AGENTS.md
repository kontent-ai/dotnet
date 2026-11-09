# AGENTS.md

Guidance for coding agents working on the **ASP.NET Core extensions**. The root `AGENTS.md` covers everything repo-wide; this file holds only what is specific to this product.

## Overview

One package, `Kontent.Ai.AspNetCore`, on `<AspNetCoreVersion>`: the `<img-asset>` and `<media-condition>` tag helpers, the `<rich-text>` tag helper, and webhook support (signature middleware, payload models, the mapping to Delivery's cache keys). It compiles against Delivery at the declared floor, so it can only call Delivery API that has shipped.

## Divergences from the siblings

- **No client and no builder.** Image options come from `Configure<ImageTransformationOptions>`. `UseWebhookSignatureValidator` takes its options from the container, an instance, a callback or an `IConfigurationSection`.
- **Image options are not validated at startup.** The package has no registration to hang `ValidateOnStart` on, and a per-tag `responsive-widths` cannot be checked before render, so a non-positive width throws from the tag helper. The webhook secret does fail at startup, from the middleware's constructor.
- **The only logging is a rejected webhook,** at `Debug`, with the reason and never a header value. The webhook extensions have no logger, so a failed asset lookup propagates rather than becoming a `false` nobody can diagnose.

## Webhooks — decided

- Kontent.ai delivers a webhook's notifications in order and retries anything but a `2xx` for up to three days, so a failed notification holds back the ones after it. `false` from `InvalidateAsync` means retry. Never return it for something a retry cannot fix, such as a cache manager that cannot purge.
- `required` stays on the model members the API always sends. `object_type`, `action` and `delivery_slot` are strings, because a value Kontent.ai adds later has to bind.
- `GetCacheDependencyKeys` is only the key mapping. The used-in lookup and the purge belong to `InvalidateAsync`, which needs the client.

## Tag helpers

- `format`, `fit` and `compression` are nullable enums, so Razor reads their values as C#: `format="ImageFormat.Webp"`, with `@using Kontent.Ai.Urls.ImageTransformation`. A bare `format="webp"` or `format="Webp"` does not compile.
- Razor still runs child tag helpers when the parent never asks for their content, so `<media-condition>` can run with no `sizes` list on the explicit-width and rendition paths.

## Live-API notes

- A deleted asset's used-in answers `200` with no items.
- A just-created asset's used-in can answer `404` for a few seconds, so `InvalidateAsync` skips created assets.

## Testing conventions

- The test project uses the Razor SDK. `Views/` holds real views, so a documented attribute form that stops compiling fails the build.
- `DocumentedWebhookSampleTests` holds hand copies of the README endpoint and upgrade guide §3. Change them together.
