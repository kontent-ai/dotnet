using System.Net;
using System.Runtime.ExceptionServices;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Extensions;

/// <summary>
/// Extension methods for converting Refit's IApiResponse to DeliveryResult.
/// </summary>
internal static class RefitApiResponseExtensions
{
    private const string ContinuationHeaderName = "X-Continuation";
    private const string StaleContentHeaderName = "X-Stale-Content";
    private const string CacheHeaderName = "X-Cache";

    /// <summary>
    /// Converts a Refit API response to a Delivery result.
    /// </summary>
    /// <typeparam name="T">The type of the response content.</typeparam>
    /// <param name="apiResponse">The Refit API response.</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    /// <returns>A delivery result containing the response data or errors.</returns>
    public static Task<IDeliveryResult<T>> ToDeliveryResultAsync<T>(this IApiResponse<T> apiResponse, ILogger? logger = null)
    {
        // Fast success path: no awaits/allocations
        if (apiResponse.IsSuccessful && apiResponse.Content is not null)
        {
            return Task.FromResult(DeliveryResult.Success(
                apiResponse.Content,
                apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty,
                StatusOf(apiResponse),
                ExtractHasStaleContent(apiResponse),
                apiResponse.Headers,
                ExtractResponseSource(apiResponse)
            ));
        }

        // Defer to the async failure/edge handler
        return MapFailureAsync(apiResponse, logger);
    }

    /// <summary>
    /// Maps a failure response to a delivery result.
    /// </summary>
    /// <typeparam name="T">The type of the response content.</typeparam>
    /// <param name="apiResponse">The API response.</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    /// <returns>A delivery result containing the response data or errors.</returns>
    private static Task<IDeliveryResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse, ILogger? logger)
    {
        RethrowIfCanceled(apiResponse.Error);

        var url = apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
        var status = StatusOf(apiResponse);
        var headers = apiResponse.Headers;
        var responseSource = ExtractResponseSource(apiResponse);

        if (apiResponse.Error is not ApiException apiEx)
        {
            var fallback = new Error
            {
                Message = "Unknown error",
                Exception = apiResponse.Error
            };
            return Task.FromResult(DeliveryResult.Failure<T>(url, status, fallback, headers, responseSource));
        }

        return MapApiExceptionAsync(apiEx, url, status, headers, responseSource, logger);

        static async Task<IDeliveryResult<T>> MapApiExceptionAsync(
            ApiException ex,
            string url,
            HttpStatusCode status,
            System.Net.Http.Headers.HttpResponseHeaders? headers,
            ResponseSource responseSource,
            ILogger? logger)
        {
            Error error;
            try
            {
                // Try to parse a structured Kontent API error from the body.
                var parsed = await ex.GetContentAsAsync<Error>().ConfigureAwait(false);
                if (parsed is not null)
                {
                    // Preserve the exception in the parsed error
                    error = parsed with { Exception = ex };
                }
                else
                {
                    error = new Error { Message = ex.Message, Exception = ex };
                }
            }
            catch (Exception parseEx) when (!IsFatalException(parseEx))
            {
                // Log the deserialization failure for diagnostics
                if (logger is not null)
                {
                    LoggerMessages.ApiErrorParsingFailed(logger, url, status, ex.Content?.Length ?? 0, parseEx);
                }

                // Body isn't JSON or deserialization failed.
                // Use Refit's formatted message as the base (includes HTTP context),
                // and append the raw response body for debugging if available.
                var rawBody = ex.Content;
                string message;

                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    // Truncate very long responses (e.g., HTML error pages) to keep the message readable
                    const int maxBodyLength = 500;
                    var truncatedBody = rawBody.Length > maxBodyLength
                        ? rawBody[..maxBodyLength] + "... (truncated)"
                        : rawBody;

                    message = $"{ex.Message} | Raw response: {truncatedBody}";
                }
                else
                {
                    message = ex.Message;
                }

                error = new Error { Message = message, Exception = ex };
            }

            return DeliveryResult.Failure<T>(url, status, error, headers, responseSource);
        }
    }

    /// <summary>
    /// Rethrows caller cancellation instead of reporting it as a failed result.
    /// </summary>
    /// <remarks>
    /// Refit captures every handler-chain exception into <c>Error</c> rather than throwing, which suits
    /// transport failures — they are an outcome of the call. Cancellation is not: it is the caller
    /// withdrawing the request. Reporting it as a result would leave <see cref="Task.IsCanceled"/> unset,
    /// so <c>Task.WhenAll</c> and <c>Parallel.ForEachAsync</c> would treat it as a failure and keep going,
    /// and <c>catch (OperationCanceledException)</c> would never fire.
    /// </remarks>
    /// <param name="error">The captured transport error, if any.</param>
    private static void RethrowIfCanceled(ApiExceptionBase? error)
    {
        if (error?.InnerException is OperationCanceledException canceled)
        {
            ExceptionDispatchInfo.Capture(canceled).Throw();
        }
    }

    /// <summary>
    /// Extracts the response source from the <c>X-Cache</c> header.
    /// </summary>
    /// <typeparam name="T">The type of the response content.</typeparam>
    /// <param name="apiResponse">The API response.</param>
    /// <returns><see cref="ResponseSource.Cdn"/> when any <c>X-Cache</c> token starts with <c>HIT</c>; otherwise <see cref="ResponseSource.Origin"/>.</returns>
    private static ResponseSource ExtractResponseSource<T>(IApiResponse<T> apiResponse)
    {
        if (apiResponse.Headers?.TryGetValues(CacheHeaderName, out var cacheValues) == true)
        {
            foreach (var cacheValue in cacheValues)
            {
                if (string.IsNullOrWhiteSpace(cacheValue))
                    continue;

                var tokens = cacheValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Any(token => token.StartsWith("HIT", StringComparison.OrdinalIgnoreCase)))
                {
                    return ResponseSource.Cdn;
                }
            }
        }

        return ResponseSource.Origin;
    }

    /// <summary>
    /// Resolves the HTTP status of a Refit response.
    /// </summary>
    /// <remarks>
    /// Null when no response was received at all — a transport-level failure. <c>default</c> rather than
    /// an invented code, because no HTTP exchange completed.
    /// </remarks>
    private static HttpStatusCode StatusOf<T>(IApiResponse<T> apiResponse) =>
        apiResponse.StatusCode ?? default;

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    /// <summary>
    /// Extracts stale content indicator from response headers.
    /// </summary>
    /// <param name="apiResponse">The API response.</param>
    /// <returns>True if content is stale.</returns>
    private static bool ExtractHasStaleContent<T>(IApiResponse<T> apiResponse)
        => apiResponse.Headers?.TryGetValues(StaleContentHeaderName, out var staleValues) == true
           && staleValues.FirstOrDefault()?.Equals("1", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Extracts continuation token from response headers.
    /// </summary>
    /// <typeparam name="T">The type of the response content.</typeparam>
    /// <param name="response">The API response.</param>
    /// <returns>The continuation token if present.</returns>
    internal static string? Continuation<T>(this IApiResponse<T> response)
        => response.Headers?.TryGetValues(ContinuationHeaderName, out var vals) == true ? vals.FirstOrDefault() : null;
}
