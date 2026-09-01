// Shared source, compiled into each SDK assembly - see src/common/README.md.

// Refit resolves through each consuming project's global usings; see src/common/README.md.

using System.Net;
using System.Runtime.ExceptionServices;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Reading a Refit response the same way in every SDK. The mapping onto each product's result type
/// stays with that product - only the parts that touch no product type live here.
/// </summary>
internal static class RefitResponses
{
    private const int MaxRawBodyLength = 500;

    /// <summary>
    /// Rethrows caller cancellation instead of reporting it as a failed result.
    /// </summary>
    /// <remarks>
    /// Refit captures a transport failure into <c>Error</c> rather than throwing, which suits it - the
    /// failure is an outcome of the call. A cancellation raised while the body is read is captured the
    /// same way, with the 2xx status already in hand, and that one is not an outcome: it is the caller
    /// withdrawing the request. (A cancellation raised during the send itself is thrown by Refit and
    /// never reaches here.) Reporting it as a result would leave <see cref="Task.IsCanceled"/> unset,
    /// so <c>Task.WhenAll</c> and <c>Parallel.ForEachAsync</c> would treat it as a failure and keep going,
    /// and <c>catch (OperationCanceledException)</c> would never fire.
    /// <para>
    /// An expired <see cref="HttpClient.Timeout"/> also surfaces as a <see cref="TaskCanceledException"/>,
    /// and that one is an outcome: the request was sent and may well have been applied. The runtime sets
    /// the <see cref="TimeoutException"/> inner exception in exactly that case - when the timeout fired and
    /// the caller's token did not - which is the same signal
    /// <see cref="HttpRetryPredicates.IsTransientException"/> classifies on.
    /// </para>
    /// </remarks>
    /// <param name="error">The captured transport error, if any.</param>
    internal static void RethrowIfCanceled(ApiExceptionBase? error)
    {
        if (error?.InnerException is OperationCanceledException canceled
            && canceled.InnerException is not TimeoutException)
        {
            ExceptionDispatchInfo.Capture(canceled).Throw();
        }
    }

    /// <summary>
    /// Resolves the HTTP status of a Refit response.
    /// </summary>
    /// <remarks>
    /// Null when no response was received at all - a transport-level failure. <c>default</c> rather than
    /// an invented code, because no HTTP exchange completed.
    /// </remarks>
    internal static HttpStatusCode StatusOf(IApiResponse response) => response.StatusCode ?? default;

    internal static string? RequestUrl(IApiResponse response) => response.RequestMessage?.RequestUri?.ToString();

    /// <summary>
    /// Caps a raw response body before it goes into an error message. Reached when the body was not the
    /// API's error envelope - an HTML 5xx page or plain text - which can be arbitrarily long.
    /// </summary>
    internal static string TruncateBody(string body) =>
        body.Length > MaxRawBodyLength ? body[..MaxRawBodyLength] + "... (truncated)" : body;
}
