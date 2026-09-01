using Kontent.Ai.Common.Http;

namespace Kontent.Ai.Management.Extensions;

/// <summary>
/// Maps Refit <see cref="IApiResponse"/> values onto <see cref="IManagementResult"/>.
/// </summary>
internal static class RefitApiResponseExtensions
{
    public static Task<IManagementResult<T>> ToManagementResultAsync<T>(this IApiResponse<T> response) =>
        response.ToManagementResultAsync(static value => value);

    public static async Task<IManagementResult<T>> ToManagementResultAsync<T>(this Task<IApiResponse<T>> responseTask) =>
        await (await responseTask.ConfigureAwait(false)).ToManagementResultAsync().ConfigureAwait(false);

    public static async Task<IManagementResult<TValue>> ToManagementResultAsync<TResponse, TValue>(
        this Task<IApiResponse<TResponse>> responseTask,
        Func<TResponse, TValue> selector) =>
        await (await responseTask.ConfigureAwait(false)).ToManagementResultAsync(selector).ConfigureAwait(false);

    public static async Task<IManagementResult> ToManagementResultAsync(this Task<IApiResponse> responseTask) =>
        await (await responseTask.ConfigureAwait(false)).ToManagementResultAsync().ConfigureAwait(false);

    // Disposing after mapping is safe on both paths: Refit fully buffers the response (the deserialized Content and
    // the ApiException's body-as-string outlive the HttpResponseMessage), and without it every response message
    // lingers until finalization.
    public static async Task<IManagementResult<TValue>> ToManagementResultAsync<TResponse, TValue>(
        this IApiResponse<TResponse> response,
        Func<TResponse, TValue> selector)
    {
        using (response)
        {
            RefitResponses.RethrowIfCanceled(response.Error);

            if (response.IsSuccessStatusCode)
            {
                if (response.Content is null)
                {
                    return ManagementResult<TValue>.Failure(
                        new Error
                        {
                            Message = "The Management API returned a success status but no readable response body.",
                            Exception = response.Error,
                        },
                        RefitResponses.StatusOf(response),
                        RefitResponses.RequestUrl(response));
                }

                return ManagementResult<TValue>.Success(
                    selector(response.Content),
                    RefitResponses.StatusOf(response),
                    RefitResponses.RequestUrl(response));
            }

            return await MapFailureAsync<TValue>(response).ConfigureAwait(false);
        }
    }

    public static async Task<IManagementResult> ToManagementResultAsync(this IApiResponse response)
    {
        using (response)
        {
            RefitResponses.RethrowIfCanceled(response.Error);

            if (response.IsSuccessStatusCode)
            {
                return ManagementResult.Success(
                    RefitResponses.StatusOf(response),
                    RefitResponses.RequestUrl(response));
            }

            return await MapFailureAsync(response).ConfigureAwait(false);
        }
    }

    private static async Task<IManagementResult<TValue>> MapFailureAsync<TValue>(IApiResponse response)
    {
        var error = await BuildErrorAsync(response).ConfigureAwait(false);
        return ManagementResult<TValue>.Failure(error, RefitResponses.StatusOf(response), RefitResponses.RequestUrl(response));
    }

    private static async Task<IManagementResult> MapFailureAsync(IApiResponse response)
    {
        var error = await BuildErrorAsync(response).ConfigureAwait(false);
        return ManagementResult.Failure(error, RefitResponses.StatusOf(response), RefitResponses.RequestUrl(response));
    }

    private static async Task<IError> BuildErrorAsync(IApiResponse response)
    {
        var parsed = await RefitErrorParsing.ParseAsync<Error>(response.Error).ConfigureAwait(false);

        return parsed switch
        {
            { Envelope: not null } => parsed.Envelope with { Exception = parsed.Exception },
            { Message: not null } => new Error { Message = parsed.Message, Exception = parsed.Exception },
            _ => new Error(),
        };
    }
}
