using System.Net;
using Kontent.Ai.Common;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Sync.SharedModels;

namespace Kontent.Ai.Sync.Extensions;

/// <summary>
/// Extension methods for converting Refit responses to sync results.
/// </summary>
internal static class RefitApiResponseExtensions
{
    private const string ContinuationHeaderName = "X-Continuation";

    /// <summary>
    /// Converts a Refit API response to a sync result.
    /// </summary>
    /// <typeparam name="T">Type of response content.</typeparam>
    /// <param name="apiResponse">Refit API response.</param>
    /// <returns>Sync result containing response data or error details.</returns>
    public static Task<ISyncResult<T>> ToSyncResultAsync<T>(this IApiResponse<T> apiResponse)
    {
        if (apiResponse.IsSuccessStatusCode && apiResponse.Content is not null)
        {
            return Task.FromResult<ISyncResult<T>>(SyncResult.Success(
                apiResponse.Content,
                RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                RefitResponses.StatusOf(apiResponse),
                ExtractSyncToken(apiResponse),
                apiResponse.Headers));
        }

        return MapFailureAsync(apiResponse);
    }

    private static Task<ISyncResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse)
    {
        RefitResponses.RethrowIfCanceled(apiResponse.Error);

        var requestUrl = RefitResponses.RequestUrl(apiResponse) ?? string.Empty;
        var statusCode = RefitResponses.StatusOf(apiResponse);
        var headers = apiResponse.Headers;

        if (apiResponse.Error is not ApiException apiException)
        {
            var fallback = new Error
            {
                Message = apiResponse.Error?.InnerException?.Message ?? apiResponse.Error?.Message ?? "Unknown error",
                ErrorCode = (int)statusCode,
                Exception = apiResponse.Error
            };

            return Task.FromResult(SyncResult.Failure<T>(requestUrl, statusCode, fallback, headers));
        }

        return MapApiExceptionAsync<T>(apiException, requestUrl, statusCode, headers);
    }

    private static async Task<ISyncResult<T>> MapApiExceptionAsync<T>(
        ApiException exception,
        string requestUrl,
        HttpStatusCode statusCode,
        System.Net.Http.Headers.HttpResponseHeaders? headers)
    {
        Error error;
        try
        {
            var parsed = await exception.GetContentAsAsync<Error>().ConfigureAwait(false);
            if (parsed is not null)
            {
                error = parsed with
                {
                    ErrorCode = parsed.ErrorCode ?? (int)statusCode,
                    Exception = exception
                };
            }
            else
            {
                error = new Error
                {
                    Message = exception.Message,
                    ErrorCode = (int)statusCode,
                    Exception = exception
                };
            }
        }
        catch (Exception parseException) when (!FatalExceptions.IsFatal(parseException))
        {
            var rawBody = exception.Content;
            var message = string.IsNullOrWhiteSpace(rawBody)
                ? exception.Message
                : $"{exception.Message} | Raw response: {RefitResponses.TruncateBody(rawBody)}";

            error = new Error
            {
                Message = message,
                ErrorCode = (int)statusCode,
                Exception = new AggregateException(exception, parseException)
            };
        }

        return SyncResult.Failure<T>(requestUrl, statusCode, error, headers);
    }

    private static string? ExtractSyncToken<T>(IApiResponse<T> apiResponse)
    {
        return apiResponse.Headers?.TryGetValues(ContinuationHeaderName, out var values) == true
            ? values.FirstOrDefault()
            : null;
    }
}
