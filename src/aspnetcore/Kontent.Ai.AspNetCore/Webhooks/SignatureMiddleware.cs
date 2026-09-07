using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.AspNetCore.Webhooks;

/// <summary>
/// Verifies signatures of Kontent.ai webhooks. Registered through <see cref="ApplicationBuilderExtensions.UseWebhookSignatureValidator(Microsoft.AspNetCore.Builder.IApplicationBuilder, Func{HttpContext, bool})"/>.
/// </summary>
internal sealed class SignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _secret;

    /// <exception cref="InvalidOperationException">
    /// <see cref="WebhookOptions.Secret"/> is not configured. Without it no signature can be verified, and
    /// continuing would admit unsigned requests. Thrown here, while the host builds its pipeline, so a
    /// misconfigured deployment fails at startup rather than at the first webhook.
    /// </exception>
    public SignatureMiddleware(RequestDelegate next, IOptions<WebhookOptions> webhookOptions)
    {
        var secret = webhookOptions.Value.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"{nameof(WebhookOptions)}.{nameof(WebhookOptions.Secret)} is not configured, so webhook " +
                "signatures cannot be verified. Set it to the secret shown in the webhook's settings in Kontent.ai.");
        }

        _next = next;
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;

        // Modern header first; the legacy one is the fallback for webhooks configured before the rename.
        // Both are verified against the same secret, so precedence only decides which is read when a
        // request carries both - but it is observable, so it is stated rather than incidental.
        var providedSignature = request.Headers["X-Kontent-ai-Signature"].FirstOrDefault()
            ?? request.Headers["X-KC-Signature"].FirstOrDefault();

        // Decoded before the body is touched: a request with no verifiable signature is rejected without
        // buffering its body, and a body that cannot be read surfaces as a 401 rather than an exception.
        if (!TryDecodeSignature(providedSignature, out var provided))
        {
            httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        // The signature covers the bytes the sender signed, so they are hashed as received from the
        // buffered stream. Decoding to a string and re-encoding would put a lossy step in the middle: the
        // decoder substitutes replacement characters for malformed input, so two different bodies can
        // re-encode to the same bytes.
        request.EnableBuffering();
        byte[] expected;
        try
        {
            expected = await HMACSHA256.HashDataAsync(_secret, request.Body, httpContext.RequestAborted);
        }
        finally
        {
            // Rewind even if the read was cancelled: anything upstream that catches the cancellation
            // and continues would otherwise hand the rest of the pipeline a mid-stream body.
            if (request.Body.CanSeek)
            {
                request.Body.Seek(0, SeekOrigin.Begin);
            }
        }

        // Compared over the raw digest bytes in constant time, not over the Base64 text: an ordinary
        // string comparison returns as soon as two characters differ, which lets a caller who can time the
        // response recover the expected signature one character at a time.
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        await _next(httpContext);
    }

    /// <summary>
    /// Decodes the header into one HMAC-SHA256 digest. Length carries no secret: the digest is always the
    /// same size, so anything that does not decode to exactly that many bytes is rejected outright.
    /// The value is normalised the way the documented sample does, because a hosting pipeline or proxy
    /// may quote it.
    /// </summary>
    private static bool TryDecodeSignature(string? header, [NotNullWhen(true)] out byte[]? signature)
    {
        signature = null;
        if (header is null)
        {
            return false;
        }

        var buffer = new byte[HMACSHA256.HashSizeInBytes];
        if (!Convert.TryFromBase64String(header.Trim().Trim('"'), buffer, out var decodedLength)
            || decodedLength != buffer.Length)
        {
            return false;
        }

        signature = buffer;
        return true;
    }
}
