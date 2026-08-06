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
            return Task.FromResult<ISyncResult<T>>(SyncResult<T>.Success(
                apiResponse.Content,
                RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                RequireSyncToken(apiResponse),
                RefitResponses.StatusOf(apiResponse),
                apiResponse.Headers));
        }

        return MapFailureAsync(apiResponse);
    }

    /// <summary>
    /// Converts a Refit response that carries no content into a sync result.
    /// </summary>
    /// <remarks>
    /// Initialization takes this path: it establishes a starting point rather than returning content, so
    /// success depends only on the status code and the token the response carries - never on a body.
    /// </remarks>
    /// <param name="apiResponse">Refit API response.</param>
    /// <returns>Sync result carrying the token, or error details.</returns>
    public static Task<ISyncResult> ToSyncResultAsync(this IApiResponse apiResponse)
    {
        if (apiResponse.IsSuccessStatusCode)
        {
            return Task.FromResult(SyncResult.Success(
                RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                RequireSyncToken(apiResponse),
                RefitResponses.StatusOf(apiResponse),
                apiResponse.Headers));
        }

        return MapFailureAsync(apiResponse);
    }

    private static async Task<ISyncResult> MapFailureAsync(IApiResponse apiResponse)
    {
        RefitResponses.RethrowIfCanceled(apiResponse.Error);

        var statusCode = RefitResponses.StatusOf(apiResponse);
        var error = await BuildErrorAsync(apiResponse, statusCode).ConfigureAwait(false);

        return SyncResult.Failure(
            RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
            statusCode,
            error,
            apiResponse.Headers);
    }

    private static async Task<ISyncResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse)
    {
        RefitResponses.RethrowIfCanceled(apiResponse.Error);

        var statusCode = RefitResponses.StatusOf(apiResponse);
        var error = await BuildErrorAsync(apiResponse, statusCode).ConfigureAwait(false);

        return SyncResult<T>.Failure(
            RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
            statusCode,
            error,
            apiResponse.Headers);
    }

    /// <summary>
    /// Builds the error for a failed response, shared by the typed and untyped paths.
    /// </summary>
    private static async Task<IError> BuildErrorAsync(IApiResponse apiResponse, HttpStatusCode statusCode)
    {
        // Only ApiException buffers the response body; the other kinds describe a failure that produced
        // no body to parse.
        if (apiResponse.Error is not ApiException exception)
        {
            return new Error
            {
                Message = apiResponse.Error?.InnerException?.Message ?? apiResponse.Error?.Message ?? "Unknown error",
                ErrorCode = (int)statusCode,
                Exception = apiResponse.Error,
            };
        }

        try
        {
            var parsed = await exception.GetContentAsAsync<Error>().ConfigureAwait(false);

            return parsed is not null
                ? parsed with { ErrorCode = parsed.ErrorCode ?? (int)statusCode, Exception = exception }
                : new Error { Message = exception.Message, ErrorCode = (int)statusCode, Exception = exception };
        }
        catch (Exception parseException) when (!FatalExceptions.IsFatal(parseException))
        {
            var rawBody = exception.Content;
            var message = string.IsNullOrWhiteSpace(rawBody)
                ? exception.Message
                : $"{exception.Message} | Raw response: {RefitResponses.TruncateBody(rawBody)}";

            return new Error
            {
                Message = message,
                ErrorCode = (int)statusCode,
                Exception = new AggregateException(exception, parseException),
            };
        }
    }

    /// <summary>
    /// Reads the continuation token every successful Sync API response carries.
    /// </summary>
    /// <remarks>
    /// The API issues a fresh token on every initialization and every delta, and it is the only way to
    /// make the next request. A successful response without one is the API breaking that contract, and
    /// there is no result worth handing back: the caller could read this page and would then be unable
    /// to continue. Failing here says so once, rather than surfacing later as a token that cannot be
    /// stored or a walk that cannot advance.
    /// </remarks>
    private static string RequireSyncToken(IApiResponse apiResponse)
    {
        var token = apiResponse.Headers?.TryGetValues(ContinuationHeaderName, out var values) == true
            ? values.FirstOrDefault()
            : null;

        return token ?? throw new InvalidOperationException(
            $"The Sync API returned a successful response without an {ContinuationHeaderName} header, " +
            $"so synchronization cannot continue. Request: {RefitResponses.RequestUrl(apiResponse)}");
    }
}
