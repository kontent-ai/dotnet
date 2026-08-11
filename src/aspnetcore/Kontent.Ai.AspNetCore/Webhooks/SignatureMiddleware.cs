using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.AspNetCore.Webhooks;

/// <summary>
/// Verifies signatures of Kontent.ai webhooks.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="webhookOptions">
/// A configuration object that allows to adjust the Kontent.ai webhook behavior. Kept private: it carries
/// the shared secret, and nothing outside this middleware has a reason to read it.
/// </param>
public sealed class SignatureMiddleware(RequestDelegate next, IOptions<WebhookOptions> webhookOptions)
{
    private readonly RequestDelegate _next = next;

    /// <summary>
    /// Processes the request to validate the webhook signature.
    /// </summary>
    /// <param name="httpContext">HTTP context whose request to inspect.</param>
    /// <returns>A task that completes when the request has been handled or rejected.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Webhooks.WebhookOptions.Secret"/> is not configured. Without it no signature can be
    /// verified, and continuing would admit unsigned requests.
    /// </exception>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        var secret = webhookOptions.Value.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"{nameof(Webhooks.WebhookOptions)}.{nameof(Webhooks.WebhookOptions.Secret)} is not configured, so webhook " +
                "signatures cannot be verified. Set it to the secret shown in the webhook's settings in Kontent.ai.");
        }

        var request = httpContext.Request;
        request.EnableBuffering();

        // The signature covers the bytes the sender signed, so they are hashed as received. Decoding to a
        // string and re-encoding would put a lossy step in the middle: the decoder substitutes replacement
        // characters for malformed input, so two different bodies can re-encode to the same bytes.
        byte[] content;
        try
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, httpContext.RequestAborted);
            content = buffer.ToArray();
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

        // Modern header first; the legacy one is the fallback for webhooks configured before the rename.
        // Both are verified against the same secret, so precedence only decides which is read when a
        // request carries both - but it is observable, so it is stated rather than incidental.
        var providedSignature = request.Headers["X-Kontent-ai-Signature"].FirstOrDefault()
            ?? request.Headers["X-KC-Signature"].FirstOrDefault();

        if (!SignatureMatches(content, secret, providedSignature))
        {
            httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        await _next(httpContext);
    }

    /// <summary>
    /// Compares the signature the request carries against one computed over its body.
    /// </summary>
    /// <remarks>
    /// The comparison runs over the raw hash bytes with <see cref="CryptographicOperations.FixedTimeEquals"/>,
    /// not over the Base64 text. An ordinary string comparison returns as soon as two characters differ,
    /// which lets a caller who can time the response recover the expected signature one character at a
    /// time. Length carries no secret here: an HMAC-SHA256 digest is always the same size, so a signature
    /// that does not decode to exactly that many bytes is rejected outright.
    /// </remarks>
    private static bool SignatureMatches(ReadOnlySpan<byte> content, string secret, string? providedSignature)
    {
        if (providedSignature is null)
        {
            return false;
        }

        Span<byte> provided = stackalloc byte[HMACSHA256.HashSizeInBytes];
        if (!Convert.TryFromBase64String(providedSignature, provided, out var decodedLength)
            || decodedLength != provided.Length)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), content, expected);

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
