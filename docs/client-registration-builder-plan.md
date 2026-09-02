# One builder per client: replacing the registration overloads

Plan for collapsing the three products' `Add…Client` overload families, their standalone builders and
their options builders into one builder shape per product, implemented once in `src/common`. Written
2026-09-02 against `unify-registration` at `f42aa55f5`, after the container move and the shared
registration plumbing landed; both are prerequisites and are assumed below.

> [!NOTE]
> **Status: implemented on `client-builders`, as a clean cut in the current release-candidate majors
> (§7).** The old overloads, builders and options builders are removed in the same commits that add
> the builder, and each changelog and upgrade guide carries the §4 table as the migration. The
> deprecation alternative is kept in §7.2 as the record of what was weighed.
>
> **Four things came out differently from the sketch below, each for a reason found while building:**
>
> 1. **`Options.Configure(...)` returns Microsoft's `OptionsBuilder<T>`, not the client builder**, so
>    several steps go in a statement lambda rather than one expression chain. That is how
>    `AddOptions` and `AddHttpClient` are used too; the examples in §2 show the chained form and the
>    docs show the statement form.
> 2. **No `TSelf` generic.** The base `ClientBuilder<TOptions>` holds the members and a protected
>    `SetResilience`; each product's sealed class implements its interface's `ConfigureResilience`
>    in two lines. Simpler, and nothing public is generic either way.
> 3. **The core is three methods, not one.** `ClientRegistration.AddClient` (name check, duplicate
>    check, validated options, the builder), `AddTransport` (one keyed generated Refit client with its
>    base address, ceiling, resilience gate, handlers and recycling - Management calls it twice, once
>    per scope, and its builder exposes both as `HttpClient` and `SubscriptionHttpClient`), and
>    `AddClientServices` (the keyed client, the factory once, the default alias). The recipe is per
>    transport and has seven members.
> 4. **`Create` throws `OptionsValidationException` in all three products, and so does the Management
>    constructor.** With the builder, options are configured lazily in the options pipeline, so there
>    is no instance to validate before the container exists; the pipeline validates on first read.
>    Delivery already documented this exception; Sync and Management change from `ValidationException`
>    and their changelogs say so.
> 5. **The model generator registers through the options-instance overload.** It consumes Delivery
>    and Management, and the default build leg compiles it against the *published* packages, which
>    do not have the builder until these majors ship and the floors are raised. `Add…Client(options)`
>    with one argument is the one call shape both surfaces accept, so the tool binds its
>    configuration section into an instance and passes that. Both legs stay green through the
>    transition; nothing about the tool's behaviour changes.
>
> One trap worth recording: the unnamed `IOptions<TOptions>` alias for the default client cannot be a
> `Configure<IOptionsMonitor<TOptions>>` registration - resolving the monitor while it is being built
> enumerates every configure registration, including that one, and the container recurses without
> bound (the Delivery test suite documents the same trap for consumer callbacks). It is an
> `IConfigureNamedOptions` that acts only on the unnamed name and builds the named options through a
> factory of its own inside `Configure`.

**Verdict: the registration surface is a cross product of independent choices written out as
overloads - 33 public `Add…Client` methods, three standalone builders, two fluent options builders,
and twelve Caching entry points - each a thin forward to one private method. The modern shape, the
one every `Microsoft.Extensions.*` package uses, is `Add` returning a builder and every choice
chained on it. That collapses each product to three `Add…Client` overloads and one `Create`, folds
the standalone builder into the same type, and lets one generic core in `src/common` implement all
three behind product-owned public interfaces. Roughly 1,100 public-surface lines become 250.**

---

## 1. The surface today

| | Delivery | Sync | Management |
|---|---|---|---|
| `Add…Client` overloads | 12 | 11 | 10 |
| Standalone builder | `DeliveryClientBuilder` (5 members) | `SyncClientBuilder` (4) | `ManagementClientBuilder` (4) |
| Fluent options builder | `IDeliveryOptionsBuilder` (13) | `ISyncOptionsBuilder` (10) | none |
| Extension package entry points | Caching: 12 on `IServiceCollection`, 6 on the builder | – | – |
| Lines in `ServiceCollectionExtensions*` | 555 | 360 | 380 |

The overloads are the product of four independent choices: how options are supplied (an instance,
a fluent builder, a delegate, a provider-aware delegate, an `IConfiguration` plus section name, an
`IConfigurationSection`), whether the client is named, and whether the two hooks are passed. Every
combination that exists is a two-line forward to the one private method that does the work, and every
combination that does not exist is a gap a consumer eventually asks for.

The standalone builders duplicate the choice a second time in a different shape (`WithOptions`,
`WithResilience`, `WithLoggerFactory`, `WithTypeProvider`, `ConfigureServices`), and the Caching
package duplicates it a third time because it must offer every registration form on both the
collection and the builder.

## 2. The target shape

### 2.1 The public surface, per product

```csharp
// Kontent.Ai.Delivery — Sync and Management are the same with their names substituted
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeliveryClient(this IServiceCollection services, Action<IDeliveryClientBuilder> configure);
    public static IServiceCollection AddDeliveryClient(this IServiceCollection services, string name, Action<IDeliveryClientBuilder> configure);
    // The one convenience kept: a pre-built instance, for tests and tools. Copies the values onto the
    // container's options; the instance itself is not registered.
    public static IServiceCollection AddDeliveryClient(this IServiceCollection services, DeliveryOptions options, Action<IDeliveryClientBuilder>? configure = null);
}

public interface IDeliveryClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
    OptionsBuilder<DeliveryOptions> Options { get; }
    IHttpClientBuilder HttpClient { get; }
    IDeliveryClientBuilder ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure);
}

public sealed partial class DeliveryClient
{
    public static DeliveryClient Create(Action<IDeliveryClientBuilder> configure);
    public static DeliveryClient Create(DeliveryOptions options, Action<IDeliveryClientBuilder>? configure = null);
}
```

That is the whole registration surface: three overloads, one interface with four properties and
one method, one static factory. `DeliveryClient.Create` builds a standalone client over a private
container; `IDeliveryClientFactory` resolves a *named* client from a container the application owns.
Different jobs, adjacent names - the README states the split in one sentence next to each.
Everything else a consumer does today is a chained call on a type Microsoft ships:

```csharp
services.AddDeliveryClient(delivery => delivery
    .Options.Configure(o => o.EnvironmentId = "…")          // was: Action<DeliveryOptions>
    .Options.Configure<IServiceProvider>((o, sp) => …)       // was: Action<IServiceProvider, DeliveryOptions>
    .Options.Bind(section)                                   // was: IConfigurationSection
    .Options.BindConfiguration("DeliveryOptions")            // was: IConfiguration + section name
    .HttpClient.ConfigurePrimaryHttpMessageHandler(…)        // was: configureHttpClient
    .ConfigureResilience(p => p.AddTimeout(…))               // was: configureResilience
    .UseMemoryCache(TimeSpan.FromMinutes(30)));              // was: AddDeliveryMemoryCache(name, …)

services.AddDeliveryClient("preview", delivery => delivery.Options.BindConfiguration("Delivery:Preview"));

await using var client = DeliveryClient.Create(delivery => delivery.Options.Configure(o => …));
```

`Options` and `HttpClient` are properties rather than wrapped methods on purpose. Exposing the
`OptionsBuilder<T>` gives `Configure`, `Configure<TDep>`, `PostConfigure`, `Bind`, `BindConfiguration`
and `Validate` for free and keeps them current with the platform; exposing the `IHttpClientBuilder`
gives every `Microsoft.Extensions.Http` extension for free and lets `HttpClient.Name` stay the SDK's
own name. Wrapping either in product methods would be the overload explosion starting again.

`ConfigureResilience` stays a method because it is the one choice the SDK must *know about*: the
`HttpClient` ceiling rule lifts the timeout only when the SDK's own pipeline bounds each attempt, so
the builder records whether a replacement was supplied and the registration reads that when the
client is first created, exactly as `configureResilience is null` does today.

`Services` and `Name` are what make extension packages cheap. `IHttpClientBuilder` exposes the same
two members for the same reason. The Caching package's twelve collection-level and six builder-level
entry points become extension methods on `IDeliveryClientBuilder` - `UseMemoryCache`,
`UseHybridCache`, `UseCacheManager` - that work identically inside `AddDeliveryClient` and inside
`DeliveryClient.Create`, because both hand out the same builder over a real service collection.

### 2.2 The standalone path is the same builder

`LoggerFactory.Create(builder => …)` hands the caller the same `ILoggingBuilder` that
`services.AddLogging(builder => …)` does, over a private collection. After the container move, that
is exactly what the SDKs' standalone builders are, so `DeliveryClient.Create(configure)` is:

```csharp
var services = new ServiceCollection();
services.AddDeliveryClient(configure);
var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
return ServiceCollectionExtensions.CreateOwnedDeliveryClient(provider, NamedClients.Default);
```

`WithLoggerFactory` becomes `delivery.Services.AddSingleton(loggerFactory)` - or, better, is not
needed at all, since `Services` is exposed and the README shows the one line. `WithTypeProvider`
becomes `delivery.Services.AddSingleton<ITypeProvider>(…)`. `ConfigureServices` is `Services` itself.
`WithResilience` is `ConfigureResilience`. The three builder types are deleted.

The explicit validation before the container is built stays where the container plan put it, so
`Create` throws `ValidationException` on invalid options in all three products. Delivery's
`DeliveryClientBuilder.Build()` documents `OptionsValidationException` today; this aligns it with the
other two, and the changelog says so.

### 2.3 The fluent options builders become extension methods on the options

`IDeliveryOptionsBuilder` and `ISyncOptionsBuilder` exist to write `UsePreviewApi(key)` instead of
setting two properties. That is worth keeping; the parallel builder type that mirrors every property
is not. Each product gets a static `…OptionsExtensions` with only the members that set *more than one
property or encode a rule*:

| Builder member | Becomes | Note |
|---|---|---|
| `UsePreviewApi(key)` | `o.UsePreviewApi(key)` | sets the key and the flag |
| `UseProductionApi(secureAccessKey)` | `o.UseProductionApi(secureAccessKey)` | sets the key and the flag |
| `UseProductionApi()` | `o.UseProductionApi()` | clears both flags |
| `WithCustomEndpoint(url)` | `o.UseCustomEndpoint(url)` | sets both endpoints |
| `WithEnvironmentId(Guid)` | `o.EnvironmentId = guid.ToString()` | a property set; no method |
| `WithTimeout`, `DisableRetryPolicy`, `WithDefaultRenditionPreset`, `WithCustomAssetDomain` | property sets | no method |
| `Build()` | – | there is nothing to build |

They are usable inside `Options.Configure(o => o.UsePreviewApi(key))` and against a bound instance
alike, which the builders were not. Sync's `UseSecureApi` is already `[Obsolete]` and is simply not
carried.

## 3. What goes into `src/common`, and what does not

The three products' builders differ only in the options type, the API type, and what handlers they
install. One internal generic core carries everything else; each product owns a two-line public
interface over it and the recipe that names its parts.

### 3.1 The core

```csharp
// src/common/Clients/ClientBuilder.cs
internal abstract class ClientBuilder<TOptions, TSelf>
    where TOptions : class
    where TSelf : ClientBuilder<TOptions, TSelf>
{
    public string Name { get; }
    public IServiceCollection Services { get; }
    public OptionsBuilder<TOptions> Options { get; }
    public IHttpClientBuilder HttpClient { get; }
    internal Action<ResiliencePipelineBuilder<HttpResponseMessage>>? Resilience { get; private set; }

    public TSelf ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Resilience = configure;
        return (TSelf)this;
    }
}
```

The curiously recurring `TSelf` is there so `ConfigureResilience` returns the product's interface
type for chaining without each product overriding it. It is the one generic trick in the design and
it is internal; nothing public is generic.

```csharp
// src/common/Clients/ClientRegistration.cs
internal readonly record struct ClientRecipe<TOptions>(
    string HttpClientNamePrefix,
    string ClientDescription,                                   // "delivery client", for error messages
    string RegistrationMethod,                                  // "AddDeliveryClient", for error messages
    Func<TOptions, Uri> BaseAddress,
    Func<TOptions, bool> ResilienceEnabled,
    Func<TOptions, TimeSpan?> Ceiling,                          // null for a product without the lift rule
    Action<ResiliencePipelineBuilder<HttpResponseMessage>> DefaultPipeline,
    Action<IHttpClientBuilder, string> AddHandlers);            // the product's handlers, in order

internal static class ClientRegistration
{
    internal static TBuilder Add<TOptions, TApi, TBuilder>(
        IServiceCollection services,
        string name,
        ClientRecipe<TOptions> recipe,
        RefitSettings refitSettings,
        Func<string, IServiceCollection, OptionsBuilder<TOptions>, IHttpClientBuilder, TBuilder> createBuilder)
        where TOptions : class where TApi : class;
}
```

`Add` does, in order, what every product's private method does today: validate the name, refuse a
duplicate, register validated options (named and default), register the keyed generated Refit client
under the product's HTTP client name, configure base address and ceiling from the options monitor,
install the options-gated resilience handler, call `AddHandlers`, apply connection recycling, and
return the builder so the consumer's chain runs after all of it. The ordering guarantee - resilience
outside the handlers, the consumer last - becomes a property of one method rather than a convention
repeated in three files.

The `ClientRecipe` has eight members. That is the honest count of what differs between the three
products, and each one is a value or a one-line delegate; none is a type parameter. It sits at the
edge of `src/common/README.md`'s rule against "unified with enough parameters", and the argument for
it is that after the last two PRs the three registration files *are* this recipe plus the same
sixty lines around it. A recipe that grew a ninth member for one product would be the signal to stop.

### 3.2 What stays in the product

- The public interface (`IDeliveryClientBuilder`) and the sealed class implementing it over the
  core - two files, under thirty lines, because the public contract must be product-owned (rule 1).
- The recipe instance: the product's HTTP client name prefix, its base-address rule, its handlers
  in order, its default pipeline. Management's recipe passes its idempotency-aware pipeline and a
  `Ceiling` that always returns the configured timeout; Delivery's and Sync's pass the shared read
  pipeline and the lift rule.
- Everything that resolves the product's own services: Delivery's `RegisterDependencies`, its
  shared `JsonSerializerOptions`, `CreateDeliveryClient`; Management's two-scope client creation.
- The `…OptionsExtensions` from §2.3.

### 3.3 What must not be attempted

- **A public generic builder.** `IClientBuilder<TOptions>` shared across products would put one
  type name in three packages and make the three snapshots depend on `src/common`. The public
  contract is per product; only the implementation is shared.
- **A type parameter for the client.** The core registers options, transport and Refit. How a
  product turns those into its client differs enough (Delivery's mapper graph, Management's two
  scopes) that a `TClient` with a construction delegate would be the nine-argument helper the
  query-builder plan already rejected once.
- **Generic handlers.** The auth handlers read product options and are product code. The recipe's
  `AddHandlers` delegate is the seam; the handlers stay where they are.

## 4. Mapping every current call site

The upgrade guides need a mechanical translation for each overload. This table is it; it is also the
checklist for the tests in §6.

| Today | Chained form |
|---|---|
| `AddDeliveryClient(Action<DeliveryOptions>)` | `AddDeliveryClient(d => d.Options.Configure(…))` |
| `AddDeliveryClient(Action<IServiceProvider, DeliveryOptions>)` | `d.Options.Configure<IServiceProvider>((o, sp) => …)` |
| `AddDeliveryClient(DeliveryOptions instance)` | unchanged in spirit: `AddDeliveryClient(instance)` stays as the third overload, now with an optional builder delegate |
| `AddDeliveryClient(Func<IDeliveryOptionsBuilder, DeliveryOptions>)` | `d.Options.Configure(o => o.UsePreviewApi(key)…)` |
| `AddDeliveryClient(IConfiguration, sectionName)` | `d.Options.BindConfiguration(sectionName)` |
| `AddDeliveryClient(IConfigurationSection)` | `d.Options.Bind(section)` |
| `AddDeliveryClient(name, …)` | `AddDeliveryClient(name, d => …)` |
| `configureHttpClient: b => …` | `d.HttpClient.…` |
| `configureResilience: p => …` | `d.ConfigureResilience(p => …)` |
| `DeliveryClientBuilder.WithOptions(…).Build()` | `DeliveryClient.Create(d => d.Options.Configure(…))` |
| `.WithLoggerFactory(f)` | `d.Services.AddSingleton(f)` |
| `.WithTypeProvider(t)` | `d.Services.AddSingleton<ITypeProvider>(t)` |
| `.ConfigureServices(s => …)` | `d.Services` |
| `.WithResilience(p => …)` | `d.ConfigureResilience(p => …)` |
| `AddDeliveryMemoryCache(name, …)` | `d.UseMemoryCache(…)` inside that client's `AddDeliveryClient` |
| `AddDeliveryHybridCache(name, …)` | `d.UseHybridCache(…)` |
| `AddDeliveryCacheManager(name, factory)` | `d.UseCacheManager(factory)` |
| `builder.WithMemoryCache(…)` | `d.UseMemoryCache(…)` inside `DeliveryClient.Create` |
| `ISyncOptionsBuilder.UseSecureApi` | already obsolete; not carried |
| `ManagementClientBuilder.WithOptions(ManagementOptions)` | `ManagementClient.Create(instance)` — `Create` mirrors the three `Add` overloads |
| `new ManagementClient(ManagementOptions)` | unchanged — kept as the constructor form of `Create` |

The `IConfiguration` plus section-name overload deserves one note: `BindConfiguration(path)` binds
lazily from the container's `IConfiguration`, which is the behaviour the current overload has via
change tokens, so reloading keeps working. The one thing lost is passing a *different*
`IConfiguration` instance than the container's; a caller who needs that uses
`d.Options.Bind(otherConfiguration.GetSection(…))`.

## 5. The extension-package contract

The Caching package today registers through `IServiceCollection` with a client name, because that
was the only seam. With the builder it registers through `IDeliveryClientBuilder`:

```csharp
public static IDeliveryClientBuilder UseMemoryCache(this IDeliveryClientBuilder builder, Action<DeliveryCacheOptions>? configure = null)
{
    builder.Services.AddKeyedSingleton<IDeliveryCacheManager>(builder.Name, (sp, _) => …);
    return builder;
}
```

Three methods replace eighteen, they read the client name off the builder instead of taking it as a
parameter, and they work in both hosting modes without a second set. The Caching package's own
snapshot changes accordingly and its changelog carries the mapping.

This is also the pattern for anything a future package adds - a telemetry decorator, a custom
handler - and the reason `Services` and `Name` are on the interface rather than hidden.

## 6. Invariants and their tests

Every behaviour below has a test today. The plan keeps each test's assertion and changes only how
the test registers the client, so the diff in the test files is the same mechanical translation as
§4 and nothing else.

| Invariant | Pinned by today |
|---|---|
| Duplicate name refused with the message naming the HTTP client | `AddXClient_DuplicateName_Throws` ×3 |
| Whitespace in a name refused | `AddXClient_InvalidName_Throws` ×3 |
| Default name resolves keyed and unkeyed to the same singleton | `…DefaultAndNamedClientAccess_IsConsistent`, `…Default_RegistersUnkeyedAndKeyedClient` |
| Named clients are independent | `…DefaultAndNamed_AreIndependent` |
| Options reload reaches the auth handler | `…RuntimeConfigurationChanges_AreReflectedInOptions` |
| Configuration binding, both forms | `…WithConfigurationSection_BindsOptions`, `…FromConfiguration_SupportsResilienceCustomizationAndStillReloads` |
| The ceiling rule, four cases, both hosting modes | `ServiceCollectionExtensionsTests` in Delivery and Sync, plus the shared `HttpClientTimeoutsTests` |
| Resilience gate: default retries, disabled does not, custom replaces | `…DefaultResilience_…`, `…ResilienceDisabled_…`, `…CustomResilience_…` |
| Handler order: resilience outside tracking outside auth; consumer last | `…RetriesGet429AndReappliesAuthAndTrackingHandlers`, `…KeepsThePooledPrimaryHandler` ×3 |
| Standalone client owns its transport; disposal fails further requests | `DisposingTheClient_ReleasesItsHttpClient` |
| `Create` throws `ValidationException` | `Build_InvalidOptions_ThrowsValidationException` ×2, `InvalidOptions_AreRejectedByBuild` |
| Caching attaches to the named client | Caching tests over `AddDeliveryMemoryCache(name, …)` |
| Subscription-only and environment-only Management clients | `…SubscriptionIdOnly_…`, `…WithoutSubscriptionId_…` |

Two tests are new: one per product that the chain order of consumer calls is honoured (a
`HttpClient.ConfigurePrimaryHttpMessageHandler` chained after `AddXClient` wins over the SDK's
recycling handler - this is what `configureHttpClient runs last` becomes), and one shared test in
`src/testing` for `ClientRegistration.Add` itself, run in all three products, pinning the
registration sequence against a recording `IServiceCollection`.

## 7. Two ways to ship it

### 7.1 Clean cut, in the current majors — **chosen**

All three products are in a release-candidate series of a major (Delivery 20, Sync 2, Management 9).
The old overloads, builders and options builders are removed; the snapshots shrink; each changelog
carries a breaking-change entry with the §4 table; each upgrade guide gains the same table.

- **For:** one surface, no dead code, no deprecation warnings for every consumer on upgrade day, and
  the Management `CLAUDE.md` is explicit that GA closes the window - this is the last release where
  the cut is a prerelease-to-prerelease change.
- **Against:** every consumer's registration line changes on the same day their target framework
  did. The translation is mechanical but it is not zero.

### 7.2 Deprecate now, remove in the next major

The new surface ships alongside the old one in the current majors. Every old overload, the three
standalone builders and the two options builders stay, marked obsolete, and delegate to the new
core so there is still one implementation:

```csharp
[Obsolete("Use AddDeliveryClient(Action<IDeliveryClientBuilder>) - chain Options.Configure(...) on the builder. Removed in 21.0.0.",
    DiagnosticId = "KAI0001", UrlFormat = "https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/upgrade-guide.md#registration")]
public static IServiceCollection AddDeliveryClient(this IServiceCollection services, Action<DeliveryOptions> configureOptions)
    => services.AddDeliveryClient(builder => builder.Options.Configure(configureOptions));
```

Three rules make this honest rather than a permanent second surface:

1. **One diagnostic id per product** (`KAI0001` Delivery, `KAI0002` Sync, `KAI0003` Management),
   so a consumer can suppress the whole family with one line while migrating, and `UrlFormat`
   points at the upgrade-guide section that holds the §4 table. This is the `ObsoleteAttribute`
   shape .NET itself uses (`SYSLIB0001`…), and it is what makes a deprecation navigable rather
   than nagging.
2. **Every obsolete member is a one-line forward to the new one.** No old code path survives;
   the old surface is a set of names. The private methods and the three `Complete…Registration`
   bodies are deleted in this mode too.
3. **The removal is scheduled in the attribute text and in a tracked issue**, and the next major's
   prepare-release checklist includes deleting `KAI000x`. A deprecation with no removal date is a
   second surface.

Two things to know about this mode:

- **The approval printer does not emit attributes**, so the snapshots will not show the
  deprecation. This was noted when `[EditorBrowsable]` went on the rich text overloads; it holds
  here too. The changelog entry is the record, and a test in each product that asserts every
  `Add…Client` overload except the two new ones carries `[Obsolete]` with the product's diagnostic
  id is the gate - cheap to write with reflection, and it fails the day someone adds an overload to
  the old family instead of the builder.
- **The Caching package** has to keep its eighteen entry points obsolete in the same way, and the
  standalone builders' `WithMemoryCache` forward to `UseMemoryCache` on the new builder. That
  doubles the Caching snapshot for one major.

- **For:** no consumer is broken on upgrade; the analyzer walks them to the new surface at their
  pace; the removal in the next major is then a deletion of forwards with a snapshot diff nobody has
  to think about.
- **Against:** one major of a doubled surface in IntelliSense, twelve more Caching methods to
  maintain as forwards, and the old builders' XML docs having to describe themselves as deprecated.

### 7.3 Decision

**7.1.** The recommendation as first written leaned to 7.2, on the grounds that adding a surface is
not breaking and the forwards are free. The counter-argument that carried: every consumer is already
on framework-upgrade day for this major, the old calls fail to compile at the exact site rather than
degrading, the §4 table makes each site a one-line rewrite, and 7.2 would have left a doubled
surface in IntelliSense for a whole major plus eighteen Caching forwards to maintain. The Management
`CLAUDE.md`'s point stands too: this is the last release where the cut is prerelease-to-prerelease.

Consequences for the rest of the plan: the deprecation step in §8 falls away and each product's
changelog carries a breaking-change entry with the §4 table; the reflection test over `[Obsolete]`
is not needed; the approval snapshots shrink and are the gate.

## 8. Sequencing

1. **`src/common`: the core.** `ClientBuilder<TOptions, TSelf>`, `ClientRecipe<TOptions>`,
   `ClientRegistration.Add`, and the shared registration-sequence test in `src/testing`. No product
   changes yet; the file compiles into nothing until step 2 includes it.
2. **Sync**, smallest surface, one scope. Add the interface, the implementation, `SyncClient.Create`,
   `SyncOptionsExtensions`; delete the existing overloads, the standalone builder and the options
   builder; migrate the tests by the §4 table. Gate: every §6 invariant green, snapshot shrunk to
   §2.1's surface and nothing else.
3. **Management**, two scopes and a public constructor. Same steps; `ManagementClient(ManagementOptions)`
   stays and calls `Create`'s internals.
4. **Delivery**, the largest surface and the only one with an extension package. Same steps, plus
   `IDeliveryClientBuilder` moving to `Kontent.Ai.Delivery.Abstractions` if the Caching package
   should compile against the interface rather than the implementation - decide in step 4, not
   before, since it changes which snapshot moves.
5. **Caching**: the three `Use…` methods; the eighteen old entry points deleted.
6. **Docs**: every README registration section, the three upgrade guides with the §4 table, the
   Delivery multi-client and caching guides, `CLAUDE.md` in Management ("two entry points" becomes
   one builder in two hosting modes), and both analysis documents' sequencing.

Steps 2–4 are independent of each other and could be reviewed separately; steps 1 and 5 are not.

## 9. Abandon criteria

1. The recipe needs a ninth member to serve one product. Then the core is a parameter list and the
   shared part should shrink to what §3.1 calls `Add`'s ordered sequence alone, with each product
   building its own builder.
2. `OptionsBuilder<T>` or `IHttpClientBuilder` cannot be exposed as a property without a consumer
   being able to break an SDK invariant that the current parameter form protects. None is known -
   the SDK's own configuration runs before the consumer's chain, as today - but a test that finds
   one is the stop.
3. In 7.2, a forward cannot be one line because the old and new behaviours differ. Then the
   difference is a behaviour change hiding in a rename and needs its own entry, not a forward.
4. The standalone `Create` cannot keep `ValidationException` for all three. It can - the container
   plan already put the explicit validation ahead of the container - but if a product's options
   validation turns out to depend on a registered service, that product keeps its own path.

## 10. Questions, closed

All three were open when this was drafted and are closed by the clean-cut decision or by a choice
recorded here, so they are not re-opened during implementation.

- **Naming.** With the old `DeliveryClientBuilder` class deleted in the same commit the interface
  arrives, there is no clash: the interfaces are `IDeliveryClientBuilder`, `ISyncClientBuilder`,
  `IManagementClientBuilder`. The shorter `I…Builder` variant was considered only as a way around
  the one-major clash that the deprecation route would have had.
- **`Create` versus the factory.** Both stay, because they do different jobs: `Create` builds a
  standalone client over a private container, `I…ClientFactory.Get(name)` resolves a named client
  from the application's container. Each README says which is which where it introduces them.
- **The options-instance overload.** Kept, as the third `Add…Client` overload and a matching
  `Create(options)`, since binding a pre-built instance is how tests and tools configure a client.
  `CopyTo` becomes public with the reflection remark it already carries.
