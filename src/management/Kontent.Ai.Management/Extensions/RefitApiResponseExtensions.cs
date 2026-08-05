using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Kontent.Ai.Management.Extensions;

/// <summary>
/// Maps Refit <see cref="IApiResponse"/> values onto <see cref="IManagementResult"/>.
/// </summary>
internal static class RefitApiResponseExtensions
{
    private const int MaxRawBodyLength = 500;

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
            RethrowIfCanceled(response.Error);

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
                        StatusOf(response),
                        RequestUrl(response));
                }

                return ManagementResult<TValue>.Success(
                    selector(response.Content),
                    StatusOf(response),
                    RequestUrl(response));
            }

            return await MapFailureAsync<TValue>(response).ConfigureAwait(false);
        }
    }

    public static async Task<IManagementResult> ToManagementResultAsync(this IApiResponse response)
    {
        using (response)
        {
            RethrowIfCanceled(response.Error);

            if (response.IsSuccessStatusCode)
            {
                return ManagementResult.Success(
                    StatusOf(response),
                    RequestUrl(response));
            }

            return await MapFailureAsync(response).ConfigureAwait(false);
        }
    }

    private static async Task<IManagementResult<TValue>> MapFailureAsync<TValue>(IApiResponse response)
    {
        var error = await BuildErrorAsync(response).ConfigureAwait(false);
        return ManagementResult<TValue>.Failure(error, StatusOf(response), RequestUrl(response));
    }

    private static async Task<IManagementResult> MapFailureAsync(IApiResponse response)
    {
        var error = await BuildErrorAsync(response).ConfigureAwait(false);
        return ManagementResult.Failure(error, StatusOf(response), RequestUrl(response));
    }

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

    private static async Task<IError> BuildErrorAsync(IApiResponse response)
    {
        if (response.Error is null)
        {
            return new Error();
        }

        // Only ApiException buffers the response body; the other ApiExceptionBase kinds describe a
        // failure that produced no body to parse.
        if (response.Error is not ApiException apiException)
        {
            return new Error { Message = response.Error.Message, Exception = response.Error };
        }

        try
        {
            var parsed = await apiException.GetContentAsAsync<Error>().ConfigureAwait(false);
            return parsed is not null
                ? parsed with { Exception = apiException }
                : new Error { Message = apiException.Message, Exception = apiException };
        }
        catch (JsonException)
        {
            // The body was not a Management API error envelope — an HTML 5xx page, plain text, or empty.
            var rawBody = apiException.Content;
            var message = string.IsNullOrWhiteSpace(rawBody)
                ? apiException.Message
                : $"{apiException.Message} | Raw response: {Truncate(rawBody)}";

            return new Error { Message = message, Exception = apiException };
        }
    }

    // StatusCode is null when no response was received at all (a transport-level failure); the fallback is
    // default (0) rather than an invented code, because no HTTP exchange completed.
    private static System.Net.HttpStatusCode StatusOf(IApiResponse response) =>
        response.StatusCode ?? default;

    private static string? RequestUrl(IApiResponse response) =>
        response.RequestMessage?.RequestUri?.ToString();

    private static string Truncate(string body) =>
        body.Length > MaxRawBodyLength ? body[..MaxRawBodyLength] + "... (truncated)" : body;
}
