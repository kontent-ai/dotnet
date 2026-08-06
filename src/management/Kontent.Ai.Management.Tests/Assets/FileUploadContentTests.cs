using AwesomeAssertions;
using Kontent.Ai.Management.Models.Assets;
using System.Text;

namespace Kontent.Ai.Management.Tests.Assets;

// Guards upload retry-safety: the resilience pipeline re-sends the same content on retry, so the body must survive
// being read more than once. A one-shot StreamContent would yield an empty second read; FileUploadContent re-reads.
public class FileUploadContentTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("Hello world from CM API .NET SDK");

    private static async Task<byte[]> SerializeAsync(HttpContent content)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task ByteArraySource_ReReadsFullBodyOnEveryAttempt()
    {
        var content = new FileUploadContent(new FileContentSource(Payload, "hello.txt", "text/plain"));

        var first = await SerializeAsync(content);
        var second = await SerializeAsync(content); // simulates a pipeline retry re-sending the request

        first.Should().Equal(Payload);
        second.Should().Equal(Payload);
    }

    [Fact]
    public async Task SeekableStreamSource_RewindsAndReReadsOnEveryAttempt()
    {
        var content = new FileUploadContent(new FileContentSource(new MemoryStream(Payload), "hello.txt", "text/plain"));

        var first = await SerializeAsync(content);
        var second = await SerializeAsync(content);

        first.Should().Equal(Payload);
        second.Should().Equal(Payload);
    }

    [Fact]
    public void SeekableSource_ReportsContentLength()
    {
        var content = new FileUploadContent(new FileContentSource(Payload, "hello.txt", "text/plain"));

        content.Headers.ContentLength.Should().Be(Payload.Length);
    }

    [Fact]
    public void NonSeekableSource_IsRefusedWhereItIsPassedIn()
    {
        // Without a length the request goes out chunked, and the endpoint rejects that as "bigger than the
        // maximal allowed limit (2 GB)" whatever the real size — so this could never have produced an upload.
        var act = () => new FileContentSource(new NonSeekableStream(Payload), "hello.txt", "text/plain");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*seekable*")
            .And.ParamName.Should().Be("stream");
    }

    [Fact]
    public void EverySource_DeclaresAContentLength()
    {
        // The endpoint needs one; nothing that reaches the wire may be missing it.
        using var file = new MemoryStream(Payload);

        new FileUploadContent(new FileContentSource(Payload, "a.txt", "text/plain"))
            .Headers.ContentLength.Should().Be(Payload.Length);
        new FileUploadContent(new FileContentSource(file, "a.txt", "text/plain"))
            .Headers.ContentLength.Should().Be(Payload.Length);
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
