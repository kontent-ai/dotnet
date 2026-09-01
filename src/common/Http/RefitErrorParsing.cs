// Shared source, compiled into each SDK assembly - see src/common/README.md.

// Refit resolves through each consuming project's global usings; see src/common/README.md.

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Reads a failed Refit response's body as the product's error envelope. The envelope is the product's
/// own type, supplied as a type argument, so nothing here names one.
/// </summary>
internal static class RefitErrorParsing
{
    /// <summary>
    /// What reading the body produced: the parsed envelope when the body was one, otherwise a message
    /// describing the failure. <see cref="Message"/> is null only when there was no error to describe,
    /// leaving the product's own default to apply.
    /// </summary>
    internal readonly record struct ParsedError<TError>(TError? Envelope, string? Message, Exception? Exception)
        where TError : class;

    /// <param name="error">The transport error Refit captured, if any.</param>
    /// <param name="onParseFailure">Invoked when the body was not the error envelope, for diagnostics.</param>
    internal static async Task<ParsedError<TError>> ParseAsync<TError>(
        ApiExceptionBase? error,
        Action<Exception>? onParseFailure = null)
        where TError : class
    {
        if (error is null)
        {
            return default;
        }

        // Only ApiException buffers the response body; the other kinds describe a failure that produced no
        // body to parse. The inner message is the one worth showing - the outer is Refit's own wrapper.
        if (error is not ApiException apiException)
        {
            return new ParsedError<TError>(null, error.InnerException?.Message ?? error.Message, error);
        }

        try
        {
            var envelope = await apiException.GetContentAsAsync<TError>().ConfigureAwait(false);

            return new ParsedError<TError>(envelope, apiException.Message, apiException);
        }
        catch (Exception parseException) when (!FatalExceptions.IsFatal(parseException))
        {
            onParseFailure?.Invoke(parseException);

            var body = apiException.Content;
            var message = string.IsNullOrWhiteSpace(body)
                ? apiException.Message
                : $"{apiException.Message} | Raw response: {RefitResponses.TruncateBody(body)}";

            return new ParsedError<TError>(null, message, apiException);
        }
    }
}
