# Shared test source

Test infrastructure that more than one test project needs an identical copy of.

**Nothing here ships.** It is compiled into test assemblies only — all of which set `IsPackable=false`
— so unlike `src/common`, none of it reaches a package. It is not a product and does not appear in
`eng/products.json`.

## Why it exists

The public-API approval printer had been copy-pasted into five test projects and then drifted, which
defeated the point of having it: Management's copy emitted fields and consts while the other four did
not, so a public constant could change in those packages without tripping their gate. A guard rail that
differs per product is not a guard rail.

## Rules

1. **One behaviour, everywhere.** The value here is that every product's gate answers the same
   question. A per-product tweak belongs in that product's test, not in this file.
2. **Deterministic output.** Reflection does not promise a stable order for interfaces or members, and
   culture-sensitive string ordering is not stable across machines. Everything rendered here sorts
   explicitly and ordinally, or it will produce snapshot diffs that have nothing to do with the code.
3. **No product types.** Same rule as `src/common`, for the same reason.

## Consuming a file

`$(KontentTestingPath)` is defined in the root `Directory.Build.props`.

```xml
<ItemGroup>
  <Compile Include="$(KontentTestingPath)PublicApiApproval.cs" Link="Testing\PublicApiApproval.cs" />
</ItemGroup>
```

## Shared tests for shared source

`Http/` holds one test file per `src/common/Http` file: `HttpRetryPredicatesTests`,
`HttpRetryDelayTests`, `SdkTrackingHeadersTests`, `DefaultResilienceTests`,
`HttpClientTimeoutsTests`. Each product's test project includes the files for the source files its
product compiles - Management takes only the first and third, because it compiles neither the
read-side delay and pipeline nor the timeout rule - so the same assertions run against each
assembly's own copy, and a change to shared code cannot pass one product's suite while breaking
another's.

The rule is the same as for the printer: a shared test file pins the shared file and nothing else.
What a product composes out of it - its default pipeline, its idempotency rule, how it reads its
own source-tracking attribute - stays in that product's tests. A shared test names no product type;
the SDK assembly under test is `typeof(SdkTrackingHeaders).Assembly`, whichever assembly the file
was compiled into.
