using System.Text.Json;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.SharedModels;
using Refit;

namespace Kontent.Ai.Sync.Extensions;

/// <summary>
/// Extension methods for converting Refit API responses to Sync results.
/// </summary>
public static class RefitApiResponseExtensions
{
    /// <summary>
    /// Converts a Refit API response to a Sync result.
    /// </summary>
    /// <typeparam name="T">The type of the response content.</typeparam>
    /// <param name="apiResponse">The Refit API response.</param>
    /// <returns>A sync result containing the response data or error.</returns>
    public static Task<ISyncResult<T>> ToSyncResultAsync<T>(this IApiResponse<T> apiResponse)
    {
        // Fast path for success (no async overhead)
        if (apiResponse.IsSuccessStatusCode && apiResponse.Content is not null)
        {
            return Task.FromResult<ISyncResult<T>>(SyncResult.Success(
                value: apiResponse.Content,
                requestUrl: apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty,
                statusCode: (int)apiResponse.StatusCode,
                syncToken: ExtractSyncToken(apiResponse)));
        }

        // Defer to failure handler
        return Task.FromResult<ISyncResult<T>>(MapFailure(apiResponse));
    }

    /// <summary>
    /// Extracts the sync token from the X-Continuation header.
    /// </summary>
    /// <param name="apiResponse">The API response.</param>
    /// <returns>The sync token if present, otherwise null.</returns>
    private static string? ExtractSyncToken<T>(IApiResponse<T> apiResponse)
    {
        if (apiResponse.Headers.TryGetValues("X-Continuation", out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Maps a failed API response to a sync result.
    /// </summary>
    /// <typeparam name="T">The type of the response content.</typeparam>
    /// <param name="apiResponse">The API response.</param>
    /// <returns>A failed sync result with error information.</returns>
    private static ISyncResult<T> MapFailure<T>(IApiResponse<T> apiResponse)
    {
        var requestUrl = apiResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
        var statusCode = (int)apiResponse.StatusCode;
        var exception = apiResponse.Error;

        // Determine error reason based on status code and exception type
        var reason = MapErrorReason(statusCode, exception);

        // Try to deserialize error from response body
        IError? error = null;
        if (!string.IsNullOrWhiteSpace(exception?.Content))
        {
            try
            {
                error = JsonSerializer.Deserialize<Error>(exception.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Update the deserialized error with reason and inner exception
                error = new Error
                {
                    Message = error?.Message ?? exception.Content,
                    RequestId = error?.RequestId,
                    ErrorCode = error?.ErrorCode ?? statusCode,
                    SpecificCode = error?.SpecificCode,
                    Reason = reason,
                    InnerException = exception
                };
            }
            catch
            {
                // If deserialization fails, create a generic error
                error = new Error
                {
                    Message = exception.Content,
                    ErrorCode = statusCode,
                    Reason = reason,
                    InnerException = exception
                };
            }
        }
        else
        {
            // No error content, create a generic error
            error = new Error
            {
                Message = apiResponse.ReasonPhrase ?? exception?.Message ?? "An unknown error occurred.",
                ErrorCode = statusCode,
                Reason = reason,
                InnerException = exception
            };
        }

        return SyncResult.Failure<T>(requestUrl, statusCode, error);
    }

    /// <summary>
    /// Maps HTTP status codes and exception types to error reasons.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="exception">The exception that occurred, if any.</param>
    /// <returns>The appropriate error reason.</returns>
    private static SyncErrorReason MapErrorReason(int statusCode, ApiException? exception)
    {
        // Check exception type first
        if (exception is not null)
        {
            var exceptionMessage = exception.Message.ToLowerInvariant();

            // Network-related errors
            if (exception.InnerException is HttpRequestException ||
                exception.InnerException is System.Net.Sockets.SocketException ||
                exceptionMessage.Contains("connection") ||
                exceptionMessage.Contains("network"))
            {
                return SyncErrorReason.NetworkError;
            }

            // Timeout errors
            if (exception.InnerException is TaskCanceledException ||
                exception.InnerException is TimeoutException ||
                exceptionMessage.Contains("timeout"))
            {
                return SyncErrorReason.Timeout;
            }
        }

        // Map based on HTTP status code
        return statusCode switch
        {
            401 or 403 => SyncErrorReason.Unauthorized,
            404 => SyncErrorReason.NotFound,
            429 => SyncErrorReason.RateLimited,
            408 => SyncErrorReason.Timeout,
            500 => SyncErrorReason.ServerError,
            502 or 503 or 504 => SyncErrorReason.ServerError,
            >= 400 and < 500 => SyncErrorReason.InvalidResponse,
            >= 500 => SyncErrorReason.ServerError,
            _ => SyncErrorReason.Unknown
        };
    }
}
