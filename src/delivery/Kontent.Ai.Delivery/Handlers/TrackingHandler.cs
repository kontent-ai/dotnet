using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Handlers;

/// <summary>
/// Adds the SDK and source tracking headers (<c>X-KC-SDKID</c> and optional <c>X-KC-SOURCE</c>) to every outgoing request.
/// </summary>
internal sealed class TrackingHandler(ILogger<TrackingHandler>? logger = null) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.AddSdkTrackingHeader();
        request.Headers.AddSourceTrackingHeader();

        if (logger is not null)
        {
            LoggerMessages.HttpTrackingHeadersAdded(logger, HttpRequestHeadersExtensions.GetSdkVersion());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
