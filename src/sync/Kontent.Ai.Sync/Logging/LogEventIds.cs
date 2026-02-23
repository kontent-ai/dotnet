using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Logging;

/// <summary>
/// Event IDs for structured logging in the Sync SDK.
/// </summary>
internal static class LogEventIds
{
    // HTTP handler events (1200-range)
    internal static readonly EventId HttpAuthSet = new(1200, nameof(HttpAuthSet));
    internal static readonly EventId HttpAuthCleared = new(1201, nameof(HttpAuthCleared));
    internal static readonly EventId HttpEndpointRewritten = new(1202, nameof(HttpEndpointRewritten));
    internal static readonly EventId HttpEnvironmentIdInjected = new(1203, nameof(HttpEnvironmentIdInjected));
    internal static readonly EventId HttpTrackingHeadersAdded = new(1204, nameof(HttpTrackingHeadersAdded));

    // API error events (1100-range)
    internal static readonly EventId ApiErrorParsingFailed = new(1100, nameof(ApiErrorParsingFailed));

    // Sync pagination events (1300-range)
    internal static readonly EventId SyncPaginationProgress = new(1300, nameof(SyncPaginationProgress));
    internal static readonly EventId SyncPaginationCompleted = new(1301, nameof(SyncPaginationCompleted));
}
