# Shared source

Infrastructure that the Delivery, Management and Sync SDKs each need an identical copy of.

**This is not a product and not a package.** It has no project file, no version, no changelog, and
it does not appear in `eng/products.json`. The files here are compiled *into* each SDK assembly as
`internal` types via `<Compile Include>`, the same way `src/libraries/Common` works in dotnet/runtime.

## Why source and not a `Kontent.Ai.Core` package

The three SDKs version and release independently (`eng/Versions.props`, tag-routed releases). A shared
runtime package would sit at the bottom of that graph and every product would have to declare a floor
on it — so each change to shared code would become: release core, then a separate PR raising three
floors (see the rules at the top of `Directory.Packages.props`), then release three SDKs.

Shared *source* costs none of that, and it has a property a shared package does not: behaviour is
frozen at the version each SDK shipped. Patching a shared package would silently change the runtime
behaviour of already-released SDKs for any consumer resolving a higher patch.

The trade is that a fix here does not reach consumers until each SDK ships. That is the intended
trade — three tags from one pull request is exactly what the monorepo bought.

## Rules

1. **Internal only.** Nothing here may be `public`. These types are duplicated into three assemblies;
   making one public would put the same type name in three packages. Public contracts stay in their
   product, even when they look identical (see the `IError` triplet).
2. **No product types.** A file here may not reference `DeliveryOptions`, `ISyncClient`,
   `ManagementClient` or anything else product-specific. If a helper needs product context, it takes
   it as a parameter. That constraint is what keeps this directory from growing into a framework.
3. **File granularity is opt-in.** Each project lists the files it uses explicitly. A file included by
   two of the three products is normal and correct — do not add a member to a shared file just so a
   third product can compile it unused.
4. **Deliberate divergence stays visible.** Where a product genuinely needs different behaviour
   (Management's idempotency-aware retry, its omitted per-attempt timeout), it keeps its own code and
   says why. Shared source is for what is *identical*, not for what could be unified with enough
   parameters.

## Consuming a file

`$(KontentCommonPath)` is defined in the root `Directory.Build.props`.

```xml
<ItemGroup>
  <Compile Include="$(KontentCommonPath)Http\HttpRetryPredicates.cs" Link="Common\HttpRetryPredicates.cs" />
</ItemGroup>
```

The `Link` keeps the file visible under a `Common` folder in the IDE instead of at the project root.

`RefitResponses.cs` takes `Refit` from the consuming project's `GlobalUsings.cs` rather than declaring
its own `using`, because all three SDKs already declare it globally and a duplicate would warn as
IDE0005 in every one of them. A project that includes the file without that global using gets a compile
error naming the missing type.
