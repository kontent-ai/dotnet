using System.Net;
using System.Net.Http.Headers;

namespace Kontent.Ai.Management.Models.Assets;

// Retry-safe upload body. The resilience pipeline re-sends the same request on retry, which a one-shot
// StreamContent cannot survive (its stream is already consumed) — a blind retry would upload an empty or partial
// file. This content re-reads the source on every send attempt instead: byte[]/file sources open a fresh stream
// each time; a caller-supplied stream is rewound. Every source is seekable — FileContentSource refuses anything
// else, because the endpoint rejects a request with no Content-Length — so the length is always known.
internal sealed class FileUploadContent : HttpContent
{
    private readonly FileContentSource _source;

    public FileUploadContent(FileContentSource source)
    {
        _source = source;
        Headers.ContentType = MediaTypeHeaderValue.Parse(source.ContentType);
        Length = GetKnownLength(source);
    }

    private long Length { get; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var source = _source.OpenReadStream();
        try
        {
            if (!_source.CreatesNewStream)
            {
                source.Position = 0;
            }

            await source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (_source.CreatesNewStream)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = Length;
        return true;
    }

    private static long GetKnownLength(FileContentSource source)
    {
        if (!source.CreatesNewStream)
        {
            return source.OpenReadStream().Length;
        }

        using var stream = source.OpenReadStream();
        return stream.Length;
    }
}
