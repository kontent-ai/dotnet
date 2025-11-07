namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the result of a Sync API operation.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
public interface ISyncResult<out T>
{
    /// <summary>
    /// Gets the result value when the operation was successful.
    /// </summary>
    T Value { get; }

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
    int StatusCode { get; }

    /// <summary>
    /// Gets the synchronization token for the next sync operation.
    /// This token is extracted from the X-Continuation header.
    /// </summary>
    string? SyncToken { get; }

    /// <summary>
    /// Gets the URL used to retrieve this response for debugging purposes.
    /// </summary>
    string? RequestUrl { get; }

    /// <summary>
    /// Gets a value indicating whether more changes are available.
    /// Returns true if any entity collection (items, types, languages, taxonomies)
    /// has reached the maximum items per response, suggesting additional data may be available
    /// via subsequent sync requests.
    /// </summary>
    bool HasMoreChanges { get; }
}
