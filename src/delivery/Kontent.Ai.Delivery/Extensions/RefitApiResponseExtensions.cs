using Kontent.Ai.Common;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Extensions;

/// <summary>
/// Extension methods for converting Refit's IApiResponse to DeliveryResult.
/// </summary>
// Every path disposes the response. Doing so while the result still carries its headers is safe:
// HttpResponseMessage.Dispose releases the content, not the header collections, and Refit has already
// buffered everything else the result needs - the deserialized Content and the ApiException's
// body-as-string both outlive the message. Without it, every response lingers until finalization.
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
            using (apiResponse)
            {
                return Task.FromResult(DeliveryResult.Success(
                    apiResponse.Content,
                    RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                    RefitResponses.StatusOf(apiResponse),
                    ExtractHasStaleContent(apiResponse),
                    apiResponse.Headers,
                    ExtractResponseSource(apiResponse)
                ));
            }
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
    private static async Task<IDeliveryResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse, ILogger? logger)
    {
        using (apiResponse)
        {
            RefitResponses.RethrowIfCanceled(apiResponse.Error);

            var url = RefitResponses.RequestUrl(apiResponse) ?? string.Empty;
            var status = RefitResponses.StatusOf(apiResponse);
            var headers = apiResponse.Headers;
            var responseSource = ExtractResponseSource(apiResponse);

            if (apiResponse.Error is not ApiException apiEx)
            {
                var fallback = new Error
                {
                    Message = "Unknown error",
                    Exception = apiResponse.Error
                };
                return DeliveryResult.Failure<T>(url, status, fallback, headers, responseSource);
            }

            Error error;
            try
            {
                // Try to parse a structured Kontent API error from the body.
                var parsed = await apiEx.GetContentAsAsync<Error>().ConfigureAwait(false);
                if (parsed is not null)
                {
                    // Preserve the exception in the parsed error
                    error = parsed with { Exception = apiEx };
                }
                else
                {
                    error = new Error { Message = apiEx.Message, Exception = apiEx };
                }
            }
            catch (Exception parseEx) when (!FatalExceptions.IsFatal(parseEx))
            {
                // Log the deserialization failure for diagnostics
                if (logger is not null)
                {
                    LoggerMessages.ApiErrorParsingFailed(logger, url, status, apiEx.Content?.Length ?? 0, parseEx);
                }

                // Body isn't JSON or deserialization failed. Use Refit's formatted message as the base
                // (it includes HTTP context) and append the raw body for debugging if available.
                var rawBody = apiEx.Content;
                var message = string.IsNullOrWhiteSpace(rawBody)
                    ? apiEx.Message
                    : $"{apiEx.Message} | Raw response: {RefitResponses.TruncateBody(rawBody)}";

                error = new Error { Message = message, Exception = apiEx };
            }

            return DeliveryResult.Failure<T>(url, status, error, headers, responseSource);
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
    {
        if (response.Headers?.TryGetValues(ContinuationHeaderName, out var values) != true || values is null)
        {
            return null;
        }

        // A present-but-empty header means no next page, same as an absent one. Normalizing here keeps every caller
        // honest about "null when the walk is finished" instead of each re-deriving it.
        var token = values.FirstOrDefault();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
