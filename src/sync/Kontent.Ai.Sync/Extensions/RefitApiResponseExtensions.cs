using Kontent.Ai.Common.Http;
using Kontent.Ai.Sync.SharedModels;

namespace Kontent.Ai.Sync.Extensions;

/// <summary>
/// Extension methods for converting Refit responses to sync results.
/// </summary>
// Every path disposes the response. Doing so while the result still carries its headers is safe:
// HttpResponseMessage.Dispose releases the content, not the header collections, and Refit has already
// buffered everything else the result needs - the deserialized Content and the ApiException's
// body-as-string both outlive the message. Without it, every response lingers until finalization.
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
        if (apiResponse.IsSuccessStatusCode)
        {
            using (apiResponse)
            {
                return Task.FromResult(apiResponse.Content is null
                    ? UnreadableBody<T>(apiResponse)
                    : SyncResult<T>.Success(
                        apiResponse.Content,
                        RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                        RequireSyncToken(apiResponse),
                        RefitResponses.StatusOf(apiResponse),
                        apiResponse.Headers));
            }
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
            using (apiResponse)
            {
                return Task.FromResult(SyncResult.Success(
                    RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                    RequireSyncToken(apiResponse),
                    RefitResponses.StatusOf(apiResponse),
                    apiResponse.Headers));
            }
        }

        return MapFailureAsync(apiResponse);
    }

    /// <summary>
    /// A 2xx whose body did not deserialize - a delta that does not match the models, most likely.
    /// Reported as a failed result naming the cause, rather than as an empty success.
    /// </summary>
    /// <remarks>
    /// The one success-status path Refit can reach with an error attached, so it is also where a
    /// cancellation raised while the body was being read arrives - and that one is thrown, not reported.
    /// </remarks>
    private static ISyncResult<T> UnreadableBody<T>(IApiResponse<T> apiResponse)
    {
        RefitResponses.RethrowIfCanceled(apiResponse.Error);

        var cause = apiResponse.Error?.InnerException?.Message ?? apiResponse.Error?.Message;

        return SyncResult<T>.Failure(
            RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
            RefitResponses.StatusOf(apiResponse),
            new Error
            {
                Message = cause is null
                    ? "The Sync API returned a success status but no readable response body."
                    : $"The Sync API returned a success status but its response body could not be read: {cause}",
                Exception = apiResponse.Error,
            },
            apiResponse.Headers);
    }

    private static async Task<ISyncResult> MapFailureAsync(IApiResponse apiResponse)
    {
        using (apiResponse)
        {
            RefitResponses.RethrowIfCanceled(apiResponse.Error);

            var statusCode = RefitResponses.StatusOf(apiResponse);
            var error = await BuildErrorAsync(apiResponse).ConfigureAwait(false);

            return SyncResult.Failure(
                RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                statusCode,
                error,
                apiResponse.Headers);
        }
    }

    private static async Task<ISyncResult<T>> MapFailureAsync<T>(IApiResponse<T> apiResponse)
    {
        using (apiResponse)
        {
            RefitResponses.RethrowIfCanceled(apiResponse.Error);

            var statusCode = RefitResponses.StatusOf(apiResponse);
            var error = await BuildErrorAsync(apiResponse).ConfigureAwait(false);

            return SyncResult<T>.Failure(
                RefitResponses.RequestUrl(apiResponse) ?? string.Empty,
                statusCode,
                error,
                apiResponse.Headers);
        }
    }

    /// <summary>
    /// Builds the error for a failed response, shared by the typed and untyped paths.
    /// </summary>
    private static async Task<IError> BuildErrorAsync(IApiResponse apiResponse)
    {
        var parsed = await RefitErrorParsing.ParseAsync<Error>(apiResponse.Error).ConfigureAwait(false);

        return parsed switch
        {
            { Envelope: not null } => parsed.Envelope with { Exception = parsed.Exception },
            { Message: not null } => new Error { Message = parsed.Message, Exception = parsed.Exception },
            _ => new Error(),
        };
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
