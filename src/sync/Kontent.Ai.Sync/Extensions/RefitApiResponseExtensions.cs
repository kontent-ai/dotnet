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

        // Try to deserialize error from response body
        IError? error = null;
        if (!string.IsNullOrWhiteSpace(apiResponse.Error?.Content))
        {
            try
            {
                error = JsonSerializer.Deserialize<Error>(apiResponse.Error.Content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                // If deserialization fails, create a generic error
                error = new Error
                {
                    Message = apiResponse.Error?.Content ?? "An unknown error occurred.",
                    ErrorCode = statusCode
                };
            }
        }
        else
        {
            // No error content, create a generic error
            error = new Error
            {
                Message = apiResponse.ReasonPhrase ?? "An unknown error occurred.",
                ErrorCode = statusCode
            };
        }

        return SyncResult.Failure<T>(requestUrl, statusCode, error);
    }
}
