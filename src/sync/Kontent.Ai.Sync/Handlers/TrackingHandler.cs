using System.Reflection;
using Kontent.Ai.Sync.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Handlers;

/// <summary>
/// Delegating handler that adds SDK tracking headers to outgoing requests.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TrackingHandler"/> class.
/// </remarks>
/// <param name="logger">Optional logger.</param>
internal sealed class TrackingHandler(ILogger<TrackingHandler>? logger = null) : DelegatingHandler
{
    private const string SdkIdHeaderName = "X-KC-SDKID";
    private const string SourceHeaderName = "X-KC-SOURCE";

    private static readonly string SdkVersion = GetSdkVersion();
    private static readonly string SdkId = $"nuget.org;Kontent.Ai.Sync;{SdkVersion}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(SdkIdHeaderName);
        request.Headers.Add(SdkIdHeaderName, SdkId);

        request.Headers.Remove(SourceHeaderName);
        request.Headers.Add(SourceHeaderName, SdkId);

        if (logger is not null)
        {
            LoggerMessages.HttpTrackingHeadersAdded(logger, SdkId);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static string GetSdkVersion()
    {
        var assembly = typeof(TrackingHandler).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Strip metadata suffix (e.g., "+build.123") for cleaner header values
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}
