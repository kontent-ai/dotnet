using Kontent.Ai.Common.Http;
using Kontent.Ai.Sync.SharedModels;

namespace Kontent.Ai.Sync.Extensions;

// Every path disposes the response. Doing so while the result still carries its headers is safe:
// HttpResponseMessage.Dispose releases the content, not the header collections, and Refit has already
// buffered everything else the result needs - the deserialized Content and the ApiException's
// body-as-string both outlive the message. Without it, every response lingers until finalization.
internal static class RefitApiResponseExtensions
{
    private const string ContinuationHeaderName = "X-Continuation";

    public static Task<ISyncResult<T>> ToSyncResultAsync<T>(this IApiResponse<T> apiResponse)
    {
        if (apiResponse.IsSuccessStatusCode)
        {
            using (apiResponse)
            {
                if (apiResponse.Content is null)
                {
                    return Task.FromResult(UnreadableBody(apiResponse));
                }

                var requestUrl = RefitResponses.RequestUrl(apiResponse) ?? string.Empty;
                var statusCode = RefitResponses.StatusOf(apiResponse);

                return Task.FromResult(ReadSyncToken(apiResponse) is { } syncToken
                    ? SyncResult<T>.Success(apiResponse.Content, requestUrl, syncToken, statusCode, apiResponse.Headers)
                    : SyncResult<T>.Failure(requestUrl, statusCode, MissingToken(), apiResponse.Headers));
            }
        }

        return MapFailureAsync(apiResponse);
    }

    // Initialization takes this path: it establishes a starting point rather than returning content, so
    // success depends only on the status code and the token the response carries - never on a body.
    public static Task<ISyncResult> ToSyncResultAsync(this IApiResponse apiResponse)
    {
        if (apiResponse.IsSuccessStatusCode)
        {
            using (apiResponse)
            {
                var requestUrl = RefitResponses.RequestUrl(apiResponse) ?? string.Empty;
                var statusCode = RefitResponses.StatusOf(apiResponse);

                return Task.FromResult(ReadSyncToken(apiResponse) is { } syncToken
                    ? SyncResult.Success(requestUrl, syncToken, statusCode, apiResponse.Headers)
                    : SyncResult.Failure(requestUrl, statusCode, MissingToken(), apiResponse.Headers));
            }
        }

        return MapFailureAsync(apiResponse);
    }

    // A 2xx whose body did not deserialize. The result keeps the token the response carried: requesting the
    // same page again fails the same way, so that token is a caller's only way past it short of initializing
    // again. This is also the one success-status path Refit can reach with an error attached, so a
    // cancellation raised while the body was being read arrives here - and that one is thrown, not reported.
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
            apiResponse.Headers,
            ReadSyncToken(apiResponse));
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

    // Every successful response carries a token, and it is the only way to make the next request. A success
    // status without one is the API breaking that contract: a page the caller could read but never continue
    // from is no success, so it is reported as a failure like any other response the SDK cannot use.
    private static Error MissingToken() => new()
    {
        Message = $"The Sync API returned a success status without an {ContinuationHeaderName} header, " +
                  "so synchronization cannot continue from this response.",
    };

    private static string? ReadSyncToken(IApiResponse apiResponse)
    {
        var token = apiResponse.Headers?.TryGetValues(ContinuationHeaderName, out var values) == true
            ? values.FirstOrDefault()
            : null;

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
