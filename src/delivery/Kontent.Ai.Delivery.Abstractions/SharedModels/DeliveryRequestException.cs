using System.Net;

namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Thrown when a request made during a <see cref="DeliveryEnumeration{T}"/> walk fails.
/// </summary>
/// <remarks>
/// Single-request calls report failure through <see cref="IDeliveryResult{T}"/> and never throw this. Enumeration has
/// no single result to return, so it throws instead — the failure detail the result would have carried is exposed here.
/// <para>
/// Cancellation is not reported this way: a cancelled walk throws <see cref="OperationCanceledException"/> as usual.
/// Deserialization and mapping faults propagate as themselves rather than being relabelled as request failures.
/// </para>
/// </remarks>
public sealed class DeliveryRequestException : Exception
{
    /// <summary>
    /// Creates the exception from the failed result's detail.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="statusCode">The HTTP status of the failed response.</param>
    /// <param name="error">The API's error detail, when the response carried one.</param>
    /// <param name="requestUrl">The request URL, for diagnostics.</param>
    public DeliveryRequestException(string message, HttpStatusCode statusCode, IError? error = null, string? requestUrl = null)
        : base(message, error?.Exception)
    {
        StatusCode = statusCode;
        Error = error;
        RequestUrl = requestUrl;
    }

    /// <summary>
    /// The HTTP status code of the failed response.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The API's error detail, or <c>null</c> when the response carried none.
    /// </summary>
    public IError? Error { get; }

    /// <summary>
    /// The request URL, for diagnostics.
    /// </summary>
    public string? RequestUrl { get; }

    /// <summary>
    /// The ID of the failed request, for troubleshooting with Kontent.ai support.
    /// </summary>
    public string? RequestId => Error?.RequestId;
}
