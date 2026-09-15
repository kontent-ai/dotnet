# Assets

Uploading binaries and managing the asset records that reference them.

- [Upload and create an asset](#upload-and-create-an-asset)
- [Supported sources and ownership](#supported-sources-and-ownership)
- [Separate upload from asset creation](#separate-upload-from-asset-creation)
- [Update metadata and organize assets](#update-metadata-and-organize-assets)

## Upload and create an asset

An asset is a binary file plus its metadata, and creating one is two operations: upload the file, then
create the asset referencing it. The `CreateAssetAsync(FileContentSource, Func<FileReference, AssetCreateModel>)`
extension does both, handing your factory the `FileReference` the upload produced:

```csharp
using Kontent.Ai.Management.Extensions;

var result = await client.CreateAssetAsync(
    new FileContentSource(File.ReadAllBytes("chart.png"), "chart.png", "image/png"),
    fileReference => new AssetCreateModel
    {
        FileReference = fileReference,
        Title = "Roasting chart"
    });
```

If the upload fails, no asset is created and the upload's failure is returned.

`UpsertAssetAsync(identifier, FileContentSource, AssetUpsertModel)` is the create-or-update counterpart.

Assets can carry taxonomy terms, using the asset-type elements defined on the environment. That needs
schema that exists in your environment, so it is not part of a minimal upload:

```csharp
Elements =
[
    new AssetTaxonomyElement
    {
        Element = Reference.ByCodename("taxonomy-categories"),
        Value = [Reference.ByCodename("hello"), Reference.ByCodename("sdk")]
    }
]
```

## Supported sources and ownership

The upload endpoint needs the file size up front, so `FileContentSource` accepts only sources that can
report one.

| Source | Who disposes the stream | On a retry |
|---|---|---|
| `byte[]` | the **SDK** — it opens and disposes one per attempt | a fresh stream each attempt |
| file path | the **SDK** — same | a fresh stream each attempt |
| `Stream` | **you** — the SDK never disposes it | rewound to position 0 and replayed |

> [!IMPORTANT]
> A stream you supply **must be seekable**, and must still be open when a retry replays it. Do not
> dispose it until the call returns.
>
> A non-seekable stream is rejected at construction rather than at request time, because the endpoint
> reports a missing `Content-Length` as *"the file is bigger than the maximal allowed limit (2 GB)"*
> whatever the real size. Buffer it first, or use the `byte[]` or file-path overload.

This is also what makes every upload safe to retry: whichever source you choose, a `429` retry re-sends
the same bytes rather than a truncated body.

Uploads have no per-attempt timeout by default, and the whole call is bounded by
`ManagementOptions.Timeout` — see [retries and timeouts](configuration.md#retries-and-timeouts).

## Separate upload from asset creation

Call the two steps yourself when you want finer control — reusing one uploaded file across several
assets, for instance:

```csharp
var fileReference = (await client.UploadFileAsync(
    new FileContentSource(File.ReadAllBytes("chart.png"), "chart.png", "image/png"))).EnsureSuccess();

var result = await client.CreateAssetAsync(new AssetCreateModel
{
    FileReference = fileReference,
    Title = "Roasting chart"
});
```

## Update metadata and organize assets

| Task | Method |
|---|---|
| List assets | `ListAssetsAsync`, `ListAssetsPageAsync` |
| Get one | `GetAssetAsync` |
| Update metadata, or create-or-update | `UpsertAssetAsync` |
| Delete | `DeleteAssetAsync` |
| Renditions | `CreateAssetRenditionAsync`, `ListAssetRenditionsAsync`, `GetAssetRenditionAsync` |
| Folder hierarchy | `GetAssetFoldersAsync`, `CreateAssetFoldersAsync`, `ModifyAssetFoldersAsync` |

Asset listings can grow large, so they have page overloads — see
[pagination](requests-and-results.md#pagination).

Transforming an image for delivery — resizing, cropping, format — is the Delivery SDK's job, not this
one.
