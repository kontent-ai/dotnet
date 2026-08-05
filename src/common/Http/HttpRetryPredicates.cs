// Shared source, compiled into each SDK assembly - see src/common/README.md.

using System.Net;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Classifies HTTP outcomes as retryable, so every Kontent.ai SDK agrees on what "transient" means.
/// </summary>
internal static class HttpRetryPredicates
{
    internal static bool IsRetryableStatusCode(HttpStatusCode? statusCode)
        => statusCode is
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    internal static bool IsTransientException(Exception? exception, CancellationToken requestCancellationToken)
        => exception switch
        {
            null => false,
            // A caller-initiated cancellation is not transient; a timeout-driven one (surfaced as a bare
            // TaskCanceledException or wrapping a TimeoutException) is.
            OperationCanceledException when requestCancellationToken.IsCancellationRequested => false,
            OperationCanceledException => exception is TaskCanceledException || exception.InnerException is TimeoutException,
            HttpRequestException or TimeoutException => true,
            _ => false,
        };
}
