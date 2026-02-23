using System.Net;
using Kontent.Ai.Sync.Abstractions;
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
                apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty,
                apiResponse.StatusCode,
                ExtractSyncToken(apiResponse),
                apiResponse.Headers));
        }

        return MapFailureAsync(apiResponse);
    }

    private static Task<ISyncResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse)
    {
        var requestUrl = apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
        var statusCode = apiResponse.StatusCode;
        var headers = apiResponse.Headers;

        if (apiResponse.Error is not ApiException apiException)
        {
            var fallback = new Error
            {
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
        catch (Exception parseException) when (!IsFatalException(parseException))
        {
            var rawBody = exception.Content;
            string message;

            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                const int maxBodyLength = 500;
                var truncatedBody = rawBody.Length > maxBodyLength
                    ? rawBody[..maxBodyLength] + "... (truncated)"
                    : rawBody;

                message = $"{exception.Message} | Raw response: {truncatedBody}";
            }
            else
            {
                message = exception.Message;
            }

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

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
