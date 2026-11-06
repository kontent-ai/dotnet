using System.Net;
using System.Net.Http.Headers;

namespace Kontent.Ai.Sync.SharedModels;

internal sealed class SyncResult<T> : ISyncResult<T>
{
    public T Value { get; }

    public bool IsSuccess { get; }

    public IError? Error { get; }

    public HttpStatusCode StatusCode { get; }

    public string SyncToken { get; }

    public string? RequestUrl { get; }

    public HttpResponseHeaders? ResponseHeaders { get; }

    private SyncResult(
        bool isSuccess,
        T value,
        string requestUrl,
        HttpStatusCode statusCode,
        string? syncToken,
        IError? error,
        HttpResponseHeaders? responseHeaders)
    {
        IsSuccess = isSuccess;
        Value = value;
        RequestUrl = requestUrl;
        StatusCode = statusCode;
        SyncToken = syncToken!;
        Error = error;
        ResponseHeaders = responseHeaders;
    }

    internal static ISyncResult<T> Success(
        T value,
        string requestUrl,
        string syncToken,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        HttpResponseHeaders? responseHeaders = null)
        => new SyncResult<T>(true, value, requestUrl, statusCode, syncToken, error: null, responseHeaders);

    internal static ISyncResult<T> Failure(
        string requestUrl,
        HttpStatusCode statusCode,
        IError? error,
        HttpResponseHeaders? responseHeaders = null,
        string? syncToken = null)
        => new SyncResult<T>(false, default!, requestUrl, statusCode, syncToken, error, responseHeaders);
}

internal sealed class SyncResult : ISyncResult
{
    public bool IsSuccess { get; }

    public IError? Error { get; }

    public HttpStatusCode StatusCode { get; }

    public string SyncToken { get; }

    public string? RequestUrl { get; }

    public HttpResponseHeaders? ResponseHeaders { get; }

    private SyncResult(
        bool isSuccess,
        string requestUrl,
        HttpStatusCode statusCode,
        string? syncToken,
        IError? error,
        HttpResponseHeaders? responseHeaders)
    {
        IsSuccess = isSuccess;
        RequestUrl = requestUrl;
        StatusCode = statusCode;
        SyncToken = syncToken!;
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
        => new SyncResult(false, requestUrl, statusCode, syncToken: null, error, responseHeaders);
}
