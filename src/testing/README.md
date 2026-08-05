# Shared test source

Test infrastructure that more than one test project needs an identical copy of.

**Nothing here ships.** It is compiled into test assemblies only — all of which set `IsPackable=false`
— so unlike `src/common`, none of it reaches a package. It is not a product and does not appear in
`eng/products.json`.

## Why it exists

The public-API approval printer had been copy-pasted into five test projects and then drifted, which
defeated the point of having it: Management's copy emitted fields and consts while the other four did
not, so `SyncConstants.MaxItemsPerEntityType` could change without tripping Sync's gate. A guard rail
that differs per product is not a guard rail.

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
