using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Logging;

/// <summary>
/// Source-generated high-performance log messages for the Sync SDK.
/// </summary>
internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 1200, Level = LogLevel.Trace, Message = "Auth header set: {AuthType} for environment {EnvironmentId}")]
    internal static partial void HttpAuthSet(ILogger logger, string authType, string environmentId);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Trace, Message = "Auth header cleared")]
    internal static partial void HttpAuthCleared(ILogger logger);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug, Message = "Endpoint rewritten from {OriginalHost} to {NewHost}")]
    internal static partial void HttpEndpointRewritten(ILogger logger, string originalHost, string newHost);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Trace, Message = "Environment ID injected into path: {EnvironmentId}")]
    internal static partial void HttpEnvironmentIdInjected(ILogger logger, string environmentId);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Trace, Message = "Tracking headers added: {SdkId}")]
    internal static partial void HttpTrackingHeadersAdded(ILogger logger, string sdkId);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "Failed to parse API error response for {Url} (status {StatusCode}, body length {BodyLength})")]
    internal static partial void ApiErrorParsingFailed(ILogger logger, string url, int statusCode, int bodyLength);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Debug, Message = "Sync pagination: fetched page {PageNumber}")]
    internal static partial void SyncPaginationProgress(ILogger logger, int pageNumber);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug, Message = "Sync pagination completed: {PageCount} pages fetched")]
    internal static partial void SyncPaginationCompleted(ILogger logger, int pageCount);
}
