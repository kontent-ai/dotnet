using Microsoft.Extensions.Logging;

namespace Kontent.Ai.AspNetCore.Logging;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Webhook rejected: the request has no signature header.")]
    internal static partial void WebhookSignatureMissing(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Webhook rejected: the signature header is not a well-formed HMAC-SHA256 digest.")]
    internal static partial void WebhookSignatureMalformed(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Webhook rejected: the signature does not match the request body.")]
    internal static partial void WebhookSignatureMismatch(ILogger logger);
}
