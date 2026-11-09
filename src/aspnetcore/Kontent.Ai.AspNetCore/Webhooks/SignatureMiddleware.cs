using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kontent.Ai.AspNetCore.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.AspNetCore.Webhooks;

/// <summary>
/// Verifies signatures of Kontent.ai webhooks. Registered through <see cref="ApplicationBuilderExtensions.UseWebhookSignatureValidator(Microsoft.AspNetCore.Builder.IApplicationBuilder, Func{HttpContext, bool})"/>.
/// </summary>
internal sealed class SignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SignatureMiddleware> _logger;
    private readonly byte[] _secret;

    /// <exception cref="InvalidOperationException">
    /// <see cref="WebhookOptions.Secret"/> is not configured. Without it no signature can be verified, and
    /// continuing would admit unsigned requests. Thrown here, while the host builds its pipeline, so a
    /// misconfigured deployment fails at startup rather than at the first webhook.
    /// </exception>
    public SignatureMiddleware(RequestDelegate next, IOptions<WebhookOptions> webhookOptions, ILogger<SignatureMiddleware> logger)
    {
        var secret = webhookOptions.Value.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"{nameof(WebhookOptions)}.{nameof(WebhookOptions.Secret)} is not configured, so webhook " +
                "signatures cannot be verified. Set it to the secret shown in the webhook's settings in Kontent.ai.");
        }

        _next = next;
        _logger = logger;
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;

        // Modern header first; the legacy one is the fallback for webhooks configured before the rename.
        var providedSignature = request.Headers["X-Kontent-ai-Signature"].FirstOrDefault()
            ?? request.Headers["X-KC-Signature"].FirstOrDefault();

        // Reject missing or malformed signature headers before buffering the body.
        if (providedSignature is null)
        {
            LoggerMessages.WebhookSignatureMissing(_logger);
            httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        if (DecodeSignature(providedSignature) is not { } provided)
        {
            LoggerMessages.WebhookSignatureMalformed(_logger);
            httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        // Hashed as received: decoding to a string and re-encoding is lossy (malformed input becomes
        // replacement characters), so two different bodies could hash the same.
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

        // Constant time over the digest bytes: a string comparison of the Base64 text returns at the first
        // differing character, which a caller who can time the response can exploit.
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            LoggerMessages.WebhookSignatureMismatch(_logger);
            httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        await _next(httpContext);
    }

    // Decodes the header into one HMAC-SHA256 digest; anything that does not decode to exactly that many
    // bytes is rejected. The value is normalised like the documented sample does, because a hosting
    // pipeline or proxy may quote it.
    private static byte[]? DecodeSignature(string header)
    {
        var buffer = new byte[HMACSHA256.HashSizeInBytes];
        return Convert.TryFromBase64String(header.Trim().Trim('"'), buffer, out var decodedLength) && decodedLength == buffer.Length
            ? buffer
            : null;
    }
}
