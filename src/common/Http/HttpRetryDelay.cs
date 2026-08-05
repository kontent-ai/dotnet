// Shared source, compiled into each SDK assembly - see src/common/README.md.

using System.Net;
using Polly.Retry;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Derives a retry delay from the response.
/// </summary>
/// <remarks>
/// Not used by the Management SDK, which leaves Retry-After handling to the built-in
/// <c>ShouldRetryAfterHeader</c> - that also covers the HTTP-date form.
/// </remarks>
internal static class HttpRetryDelay
{
    /// <summary>
    /// Reads Retry-After on 429 responses, where Kontent.ai returns the seconds until the next request
    /// is allowed. Returns <c>null</c> for anything else, deferring to the configured backoff.
    /// </summary>
    internal static ValueTask<TimeSpan?> FromRetryAfterHeader(RetryDelayGeneratorArguments<HttpResponseMessage> args)
    {
        if (args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response
            && response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            return ValueTask.FromResult<TimeSpan?>(retryAfter);
        }

        return ValueTask.FromResult<TimeSpan?>(null);
    }
}
