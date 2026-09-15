# Configuring the Management client

How to register a client, where its options come from, and what the transport does by default.

- [Client registration and lifetime](#client-registration-and-lifetime)
- [Options and configuration binding](#options-and-configuration-binding)
- [Named clients](#named-clients)
- [Configuration options](#configuration-options)
- [Retries and timeouts](#retries-and-timeouts)
- [HTTP handlers](#http-handlers)
- [Source tracking for tool authors](#source-tracking-for-tool-authors)

## Client registration and lifetime

Two entry points, same builder: `AddManagementClient` for applications, `ManagementClient.Create` or the
constructor for scripts.

```csharp
services.AddManagementClient(management => management.Options.Configure(options =>
{
    options.EnvironmentId = "<YOUR_ENVIRONMENT_ID>";
    options.ApiKey = "<YOUR_API_KEY>";
}));
```

Inject `IManagementClient` wherever you need it.

**The container owns a registered client — do not dispose it.** A client you build yourself owns its
resources, so dispose it:

```csharp
await using var client = new ManagementClient(new ManagementOptions
{
    EnvironmentId = "<YOUR_ENVIRONMENT_ID>",
    ApiKey = "<YOUR_API_KEY>"
});
```

`ManagementClient.Create` takes the same builder as `AddManagementClient`, for when a script needs more
than the constructor offers:

```csharp
await using var client = ManagementClient.Create(management => management.Options.Configure(options =>
{
    options.EnvironmentId = "<YOUR_ENVIRONMENT_ID>";
    options.ApiKey = "<YOUR_API_KEY>";
}));
```

The concrete `ManagementClient` implements `IDisposable` and `IAsyncDisposable`; the `IManagementClient`
interface does not, because a resolved client belongs to the container. Invalid options throw
`OptionsValidationException` from either entry point.

The builder exposes `Options` (an `OptionsBuilder<ManagementOptions>`), `HttpClient` and
`SubscriptionHttpClient` (the `IHttpClientBuilder` for each transport scope), `ConfigureResilience`, and
`Services` / `Name`. Whatever you chain runs after the SDK's own setup.

> [!NOTE]
> `IManagementClientFactory` resolves a *named* client from your container. `ManagementClient.Create`
> builds a standalone one. Similar names, different jobs.

## Options and configuration binding

Bind from an `IConfiguration` section:

```json
{
  "ManagementOptions": {
    "EnvironmentId": "<YOUR_ENVIRONMENT_ID>",
    "ApiKey": "<YOUR_API_KEY>"
  }
}
```

```csharp
services.AddManagementClient(management =>
    management.Options.Bind(configuration.GetSection("ManagementOptions")));
```

`Options` is a standard `OptionsBuilder<ManagementOptions>`, so the rest of the options pattern is
available unchanged:

| Instead of `Bind` | Use |
|---|---|
| Section resolved from the container by name | `management.Options.BindConfiguration("Management:Production")` |
| A pre-built options instance | `services.AddManagementClient(new ManagementOptions { … })` |
| Values from another registered service | `management.Options.Configure<ISecretStore>((options, secrets) => …)` |
| Validation beyond the built-in rules | `management.Options.Validate(options => …)` |

A pre-built instance is copied onto the options the container materializes, so mutating it afterwards
has no effect once they have been read.

## Named clients

Give each registration a name to run more than one environment:

```csharp
services.AddManagementClient("production", management => management.Options.Configure(options =>
{
    options.EnvironmentId = "<PRODUCTION_ENVIRONMENT_ID>";
    options.ApiKey = "<PRODUCTION_API_KEY>";
}));

services.AddManagementClient("staging", management => management.Options.Configure(options =>
{
    options.EnvironmentId = "<STAGING_ENVIRONMENT_ID>";
    options.ApiKey = "<STAGING_API_KEY>";
}));
```

Resolve them through `IManagementClientFactory`:

```csharp
var production = clientFactory.Get("production");
var staging = clientFactory.Get("staging");
```

Names are compared ordinally. A client needing different credentials — a subscription key, for instance
— is a separate named registration, not a second key on an existing one.

## Configuration options

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `EnvironmentId` | For environment endpoints | — | The GUID of your Kontent.ai environment. Required for everything except subscription-scoped endpoints. |
| `ApiKey` | Yes | — | A Management API key for environment-scoped endpoints, or a **Subscription API key** for subscription-scoped ones. They are different keys — see [subscription-scoped operations](administration.md#subscription-scoped-operations). |
| `SubscriptionId` | For subscription endpoints | — | The subscription GUID. Required only for subscription-scoped endpoints such as user management. |
| `EnableResilience` | No | `true` | Toggles the built-in retry/backoff pipeline without uninstalling it. |
| `Timeout` | No | `30 minutes` | Ceiling on one call, covering every retry attempt and the waits between them. |
| `Endpoint` | No | `https://manage.kontent.ai` | The Management API base address. Override only when targeting a non-production endpoint. |

Options are validated on use. A missing `ApiKey`, a malformed identifier, or neither `EnvironmentId` nor
`SubscriptionId` surfaces as `OptionsValidationException` — from the constructor and `Create`, or at host
startup when the container validates.

Each scope is built only when its identifier is present, so a client scoped to one and used for the
other fails immediately, naming the missing option, rather than sending a request to a path with an
empty segment.

## Retries and timeouts

Every client comes with a resilience pipeline
([`Microsoft.Extensions.Http.Resilience`](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)):
exponential backoff with jitter, `Retry-After` handling, and **idempotency-aware retries**.

A `429` is retried for every method, because the request was rejected rather than applied. Other
transient failures — `408`, `5xx`, transport errors — are retried only for idempotent methods (`GET`,
`HEAD`, `OPTIONS`, `PUT`, `DELETE`). A `POST` or `PATCH` that fails mid-flight is never replayed, because
the write may already have landed.

Set `EnableResilience = false` to make the pipeline a passthrough.

### Replacing the pipeline

`ConfigureResilience` **replaces** the pipeline rather than adding to it, across both the
environment-scoped and subscription-scoped transports. Replacing it drops the idempotency rule, so
re-establish a write-safety rule of your own:

```csharp
var retry = new HttpRetryStrategyOptions { MaxRetryAttempts = 5 };
retry.DisableFor(HttpMethod.Post, HttpMethod.Patch);   // do not replay writes

services.AddManagementClient(management =>
{
    management.Options.Configure(options => { options.EnvironmentId = "…"; options.ApiKey = "…"; });
    management.ConfigureResilience(pipeline => pipeline
        .AddRetry(retry)
        .AddTimeout(TimeSpan.FromSeconds(30)));
});
```

> [!WARNING]
> A bare `new HttpRetryStrategyOptions()` retries **every** method on **every** transient failure, `POST`
> and `PATCH` included. Against a write API that turns one ambiguous failure into duplicate content.

`DisableFor` is **not** equivalent to the built-in rule. It excludes by method, so a rate-limited `POST`
is not retried either, where the default would have honoured `Retry-After` and backed off. That errs on
the safe side, but under the per-minute rate limit you may see writes fail that the default would have
carried through.

### Timeouts

Two clocks bound a request:

- **Per attempt** — no default. Asset and file uploads can legitimately run long, and cutting one off
  only to retry it re-uploads the same bytes. Add one through `ConfigureResilience` if you want it.
- **The whole call** — `ManagementOptions.Timeout`, covering every attempt and the waits between them.
  Defaults to **30 minutes**, sized against the 2 GB asset limit.

Prefer `Timeout` for changing the total budget; replacing the pipeline is not the way to set one. Use
`Timeout.InfiniteTimeSpan` to be bounded only by your `CancellationToken`. `Timeout` outranks
`Retry-After`: the pipeline waits exactly as long as the server asked, but the call is still cut short
if your ceiling runs out first.

Uploads have their own retry-safety rules — see [supported sources and
ownership](assets.md#supported-sources-and-ownership).

## HTTP handlers

Each transport scope has its own `IHttpClientBuilder`:

```csharp
management.HttpClient.AddHttpMessageHandler<MyAuditingHandler>();              // environment-scoped
management.SubscriptionHttpClient.AddHttpMessageHandler<MyAuditingHandler>();  // subscription-scoped
```

## Source tracking for tool authors

Every request carries two analytics headers:

- **`X-KC-SDKID`** — identifies this SDK. Always `nuget.org;Kontent.Ai.Management;<version>`. Not configurable.
- **`X-KC-SOURCE`** — identifies a library built *on top of* the SDK. Set only when a caller assembly opts in.

**End-user applications need do nothing here.** If you publish a library that wraps this SDK, add the
attribute at assembly level; at request time the SDK walks the call stack, finds your assembly and reads
it:

```csharp
using Kontent.Ai.Management.Attributes;

[assembly: SourceTrackingHeader]
```

Two overloads override what it reports: `SourceTrackingHeader("Acme.AwesomeTool")` when your package id
differs from your assembly name, and `SourceTrackingHeader("Acme.AwesomeTool", 1, 2, 3, "beta")` to pin
the version too. Use one, not all three.
