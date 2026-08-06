namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the result of a Sync API operation that produces no value of its own.
/// </summary>
/// <remarks>
/// Initialization is the case: it establishes a starting point rather than returning content, so the
/// useful output is <see cref="SyncToken"/>. Operations that do return content use
/// <see cref="ISyncResult{T}"/>.
/// </remarks>
public interface ISyncResult
{
    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// Gets the error that occurred during the operation.
    /// </summary>
    IError? Error { get; }

    /// <summary>
    /// Gets the HTTP status code of the response.
    /// </summary>
    HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the synchronization token for the next sync operation, read from the X-Continuation header.
    /// </summary>
    /// <remarks>
    /// Meaningful only when <see cref="IsSuccess"/> is <c>true</c>. The API issues a fresh token with
    /// every successful response, and a response without one fails rather than producing a result, so on
    /// a successful result this is always populated.
    /// </remarks>
    string SyncToken { get; }

    /// <summary>
    /// Gets the URL used to retrieve this response for debugging purposes.
    /// </summary>
    string? RequestUrl { get; }

    /// <summary>
    /// Gets the HTTP response headers from the Sync API.
    /// </summary>
    HttpResponseHeaders? ResponseHeaders { get; }
}

/// <summary>
/// Represents the result of a Sync API operation that returns content.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
public interface ISyncResult<out T> : ISyncResult
{
    /// <summary>
    /// Gets the result value when the operation was successful.
    /// </summary>
    /// <remarks>
    /// Meaningful only when <see cref="ISyncResult.IsSuccess"/> is <c>true</c>.
    /// </remarks>
    T Value { get; }
}
