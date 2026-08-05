using System.Net;
using System.Runtime.ExceptionServices;
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
                StatusOf(apiResponse),
                ExtractSyncToken(apiResponse),
                apiResponse.Headers));
        }

        return MapFailureAsync(apiResponse);
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

    private static Task<ISyncResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse)
    {
        RethrowIfCanceled(apiResponse.Error);

        var requestUrl = apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
        var statusCode = StatusOf(apiResponse);
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
