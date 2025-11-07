using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.SharedModels;

/// <summary>
/// Concrete implementation of <see cref="ISyncResult{T}"/> for functional error handling.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
internal sealed class SyncResult<T> : ISyncResult<T>
{
    /// <inheritdoc/>
    public T Value { get; }

    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public IError? Error { get; }

    /// <inheritdoc/>
    public int StatusCode { get; }

    /// <inheritdoc/>
    public string? SyncToken { get; }

    /// <inheritdoc/>
    public string? RequestUrl { get; }

    /// <inheritdoc/>
    public bool HasMoreChanges { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <param name="requestUrl">The request URL.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="syncToken">The sync token for next operation.</param>
    internal SyncResult(
        T value,
        string requestUrl,
        int statusCode = 200,
        string? syncToken = null)
    {
        Value = value;
        IsSuccess = true;
        StatusCode = statusCode;
        SyncToken = syncToken;
        RequestUrl = requestUrl;
        HasMoreChanges = CalculateHasMoreChanges(value);
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="requestUrl">The request URL.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="error">The error that occurred.</param>
    internal SyncResult(
        string requestUrl,
        int statusCode,
        IError? error)
    {
        Value = default!;
        IsSuccess = false;
        Error = error;
        StatusCode = statusCode;
        SyncToken = null;
        RequestUrl = requestUrl;
        HasMoreChanges = false;
    }

    /// <summary>
    /// Calculates whether more changes are available based on the response data.
    /// </summary>
    private static bool CalculateHasMoreChanges(T value)
    {
        // Only delta responses can have more changes
        if (value is not ISyncDeltaResponse deltaResponse)
        {
            return false;
        }

        // Check if any collection has reached the maximum items per response
        return deltaResponse.Items.Count >= SyncConstants.MaxItemsPerEntityType
            || deltaResponse.Types.Count >= SyncConstants.MaxItemsPerEntityType
            || deltaResponse.Languages.Count >= SyncConstants.MaxItemsPerEntityType
            || deltaResponse.Taxonomies.Count >= SyncConstants.MaxItemsPerEntityType;
    }
}

/// <summary>
/// Factory methods for creating <see cref="ISyncResult{T}"/> instances.
/// </summary>
internal static class SyncResult
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="value">The result value.</param>
    /// <param name="requestUrl">The request URL.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="syncToken">The sync token for next operation.</param>
    /// <returns>A successful result.</returns>
    public static ISyncResult<T> Success<T>(
        T value,
        string requestUrl,
        int statusCode = 200,
        string? syncToken = null)
    => new SyncResult<T>(value, requestUrl, statusCode, syncToken);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <param name="requestUrl">The request URL.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="error">The error that occurred.</param>
    /// <returns>A failed result.</returns>
    public static ISyncResult<T> Failure<T>(
        string requestUrl,
        int statusCode,
        IError? error)
    => new SyncResult<T>(requestUrl, statusCode, error);
}
