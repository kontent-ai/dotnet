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
    /// Refit captures every handler-chain exception into <c>Error</c> rather than throwing, which suits
    /// transport failures - they are an outcome of the call. Cancellation is not: it is the caller
    /// withdrawing the request. Reporting it as a result would leave <see cref="Task.IsCanceled"/> unset,
    /// so <c>Task.WhenAll</c> and <c>Parallel.ForEachAsync</c> would treat it as a failure and keep going,
    /// and <c>catch (OperationCanceledException)</c> would never fire.
    /// </remarks>
    /// <param name="error">The captured transport error, if any.</param>
    internal static void RethrowIfCanceled(ApiExceptionBase? error)
    {
        if (error?.InnerException is OperationCanceledException canceled)
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
