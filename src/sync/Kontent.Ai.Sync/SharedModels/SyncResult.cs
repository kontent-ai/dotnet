using System.Net;
using System.Net.Http.Headers;

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
    public HttpStatusCode StatusCode { get; }

    /// <inheritdoc/>
    public string SyncToken { get; }

    /// <inheritdoc/>
    public string? RequestUrl { get; }

    /// <inheritdoc/>
    public HttpResponseHeaders? ResponseHeaders { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <param name="requestUrl">The request URL.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="syncToken">The sync token for next operation.</param>
    /// <param name="responseHeaders">HTTP response headers returned by the API.</param>
    internal SyncResult(
        T value,
        string requestUrl,
        HttpStatusCode statusCode,
        string syncToken,
        HttpResponseHeaders? responseHeaders)
    {
        Value = value;
        IsSuccess = true;
        StatusCode = statusCode;
        SyncToken = syncToken;
        RequestUrl = requestUrl;
        ResponseHeaders = responseHeaders;
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="requestUrl">The request URL.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="error">The error that occurred.</param>
    /// <param name="responseHeaders">HTTP response headers returned by the API.</param>
    internal SyncResult(
        string requestUrl,
        HttpStatusCode statusCode,
        IError? error,
        HttpResponseHeaders? responseHeaders)
    {
        Value = default!;
        IsSuccess = false;
        Error = error;
        StatusCode = statusCode;
        SyncToken = null!;
        RequestUrl = requestUrl;
        ResponseHeaders = responseHeaders;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    internal static ISyncResult<T> Success(
        T value,
        string requestUrl,
        string syncToken,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        HttpResponseHeaders? responseHeaders = null)
        => new SyncResult<T>(value, requestUrl, statusCode, syncToken, responseHeaders);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    internal static ISyncResult<T> Failure(
        string requestUrl,
        HttpStatusCode statusCode,
        IError? error,
        HttpResponseHeaders? responseHeaders = null)
        => new SyncResult<T>(requestUrl, statusCode, error, responseHeaders);
}


/// <summary>
/// Concrete implementation of <see cref="ISyncResult"/> for operations that return no content.
/// </summary>
internal sealed class SyncResult : ISyncResult
{
    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public IError? Error { get; }

    /// <inheritdoc/>
    public HttpStatusCode StatusCode { get; }

    /// <inheritdoc/>
    public string SyncToken { get; }

    /// <inheritdoc/>
    public string? RequestUrl { get; }

    /// <inheritdoc/>
    public HttpResponseHeaders? ResponseHeaders { get; }

    private SyncResult(
        bool isSuccess,
        string requestUrl,
        HttpStatusCode statusCode,
        string syncToken,
        IError? error,
        HttpResponseHeaders? responseHeaders)
    {
        IsSuccess = isSuccess;
        RequestUrl = requestUrl;
        StatusCode = statusCode;
        SyncToken = syncToken;
        Error = error;
        ResponseHeaders = responseHeaders;
    }

    internal static ISyncResult Success(
        string requestUrl,
        string syncToken,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        HttpResponseHeaders? responseHeaders = null)
        => new SyncResult(true, requestUrl, statusCode, syncToken, error: null, responseHeaders);

    internal static ISyncResult Failure(
        string requestUrl,
        HttpStatusCode statusCode,
        IError? error,
        HttpResponseHeaders? responseHeaders = null)
        => new SyncResult(false, requestUrl, statusCode, syncToken: null!, error, responseHeaders);
}
