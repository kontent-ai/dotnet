# Code samples

Every .NET sample on [Kontent.ai Learn](https://kontent.ai/learn) is published from
[Kontent-ai-Learn/kontent-ai-learn-code-samples](https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net),
and every file under its `net/` folder is written here, as a test. `dotnet test` and CI therefore compile
and run each sample against the current source, so a breaking change fails the sample that shows it and
has to reach Learn with the fix.

## Where samples live

Each product keeps its samples in a `CodeSamples` folder of its test project:

| Product | Folder | Client block |
|---|---|---|
| Management SDK | `src/management/Kontent.Ai.Management.Tests/CodeSamples` | `ManagementClient` |
| Delivery SDK | `src/delivery/Kontent.Ai.Delivery.Tests/CodeSamples` | `DeliveryClient.Create` |
| Sync SDK | `src/sync/Kontent.Ai.Sync.Tests/CodeSamples` | `SyncClient.Create` |
| ASP.NET Core extensions | `src/aspnetcore/Kontent.Ai.AspNetCore.Tests/CodeSamples` | none |
| Model generator | `src/model-generator/Kontent.Ai.ModelGenerator.Tests/CodeSamples` | none |

The saved API responses the samples run against sit with the product's other fixtures, in a `CodeSamples`
subfolder.

## A sample is a marked section of a test

```csharp
[Fact]
public async Task GetItem()
{
    var client = SampleClient.Create("DeliveryClient/coffee_beverages_explained.json");   // not published

    // DocSection: delivery_api_get_item
    // Gets a strongly typed article
    var result = await client.GetItem<Article>("my_article").ExecuteAsync();
    // EndDocSection

    Assert.True(result.IsSuccess);                                                        // not published
}
```

What sits between the markers is published as `net/**/<id>.cs`. Anything the sample needs but a reader does
not see - a mocked client, a variable an earlier snippet on the Learn page defines, an assertion - goes
outside them.

- **The id is the published file name.** It is the join key with Learn, so it is used once, and a sample
  whose id has no published file is an error. To rename, rename the source, never the published file.
- **Every file published there has a section here.** A sample that has become redundant still has a Learn
  page linking it, so it is never deleted. Flag it with `// DocReview: <why>` inside the section; the line
  is not published and the sync lists every flag for follow-up.
- **No test scaffolding inside a section.** `EnsureSuccess()` is both a real check and idiomatic sample
  code, so it can stay in.
- **A sample in another language** lives in a file of that language and uses that language's comments
  (`# DocSection: <id>` in a `.sh` file). It is published as written.

## Client setup

Samples that build a client show the one in their product's `CodeSamples/ClientRegistration.cs`, marked
`// DocClient` ... `// EndDocClient`. It is compiled but never run, because the placeholders in it would
fail options validation. A product can declare more than one - `// DocClient: subscription` is Management's
client for the Subscription API - and a section picks a named one with a `// DocClientName: <name>` line,
which is not published. A sample that is about client setup - a preview or secure-access key - declares
its own client inside its section instead.

## Syncing to Learn

After a change here is merged, from the repository root:

```sh
dotnet run eng/scripts/sync-code-samples.cs -- <path-to-your-kontent-ai-learn-code-samples-checkout>
```

**The sync overwrites the published files.** A fix made directly in the samples repository is lost unless
it is brought into the section here first, so check that repository's history for .NET changes since the
last sync before running it.

The script creates `sync/dotnet-<sha>` off `origin/master` in that checkout (`--base <ref>` for another
base), writes every published file that changed and stages it. It commits nothing: review the diff, commit,
and open the pull request there. The checkout must be clean. `--check` compares without touching it.

For each published file it:

1. **Keeps the leading comments.** The `// Tip:` line and anything else above the code stay.
2. **Replaces the client setup** - the declaration and the comments directly above it - with the product's
   client block, unless the section declares its own client.
3. **Replaces the code** below it with the section.
4. **Rewrites the usings, if the file has any.** It builds the product's test project, compiles the file
   against it and writes one `using` for each namespace whose types or extension methods the code uses.
   Namespaces a .NET project imports implicitly (`System`, `System.Linq` …) and test-only ones are never
   written. A file without usings is a snippet and stays without.
