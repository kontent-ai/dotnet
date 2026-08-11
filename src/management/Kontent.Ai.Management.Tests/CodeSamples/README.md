# Code samples

The samples in this folder are tests, so they are guaranteed to compile and to run against the SDK. They
are also the source for the published documentation samples in
[Kontent-ai-Learn/kontent-ai-learn-code-samples](https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/master/net).
After merging a change here, mirror it there.

## Sections

A marked section is what gets published, so it must contain the sample and nothing else. The pair opens
below the mock client and closes above the assertions — both of those are test scaffolding and belong
outside:

```csharp
[Fact]
public async Task DeleteAsset()
{
    var client = MockClientFactory.CreateForSample(SampleFolder);   // outside

    // DocSection: cm_api_v2_delete_asset
    // Tip: Find more about .NET SDKs at https://kontent.ai/learn/net
    var identifier = Reference.ById(Guid.Parse("fcbb12e6-66a3-4672-85d9-d502d16b8d9c"));
    // var identifier = Reference.ByExternalId("which-brewing-fits-you");

    await client.DeleteAssetAsync(identifier);
    // EndDocSection

    Assert.NotNull(response);                                       // outside
}
```

The published file wraps that body in the `using` and a real `ManagementClient` construction.

Three rules, each enforced by `DocSectionMarkerTests`:

- **Every section is closed.** An unclosed one runs to the next marker and publishes everything between,
  including the next method's `[Fact]` and mock setup.
- **Every id is used once.** The id names the published file, so two sections cannot share one. It is a
  join key with that repository — check there before renaming, and reuse an existing id rather than
  inventing a variant.
- **No scaffolding inside a section.** No `MockClientFactory`, `[Fact]`, `Assert.*` or
  `Record.ExceptionAsync`. Assert *after* the closing marker; where a sample needs to show that a call
  succeeded, `EnsureSuccess()` is both a real assertion and idiomatic sample code, so it can stay inside.
