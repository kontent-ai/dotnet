using System.Reflection;
using System.Runtime.CompilerServices;
using Kontent.Ai.Common.Http;
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
    private static readonly Assembly SdkAssembly = typeof(TrackingHandler).Assembly;
    private static readonly string SdkId = SdkTrackingHeaders.ComposeSdkHeaderValue(SdkAssembly);
    private static readonly Lazy<string?> LazySource = new(ResolveSourceHeaderValue);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.SetSdkHeader(SdkId);
        request.Headers.SetSourceHeader(LazySource.Value);

        if (logger is not null)
        {
            LoggerMessages.HttpTrackingHeadersAdded(logger, SdkId);
        }

        return base.SendAsync(request, cancellationToken);
    }

    internal static string ComposeSourceHeaderValue(Assembly originatingAssembly, SyncSourceTrackingHeaderAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(originatingAssembly);
        ArgumentNullException.ThrowIfNull(attribute);

        string? packageName;
        string version;

        if (attribute.LoadFromAssembly)
        {
            packageName = attribute.PackageName ?? originatingAssembly.GetName().Name;
            version = originatingAssembly.GetProductVersion();
        }
        else
        {
            packageName = attribute.PackageName;
            var preRelease = string.IsNullOrEmpty(attribute.PreReleaseLabel) ? "" : $"-{attribute.PreReleaseLabel}";
            version = $"{attribute.MajorVersion}.{attribute.MinorVersion}.{attribute.PatchVersion}{preRelease}";
        }

        return $"{packageName};{version}";
    }

    // Must never throw: the result is cached in a Lazy, so a thrown exception would be cached and rethrown on
    // every subsequent request for the process lifetime - hence the blanket catch; the header is simply omitted.
    private static string? ResolveSourceHeaderValue()
    {
        try
        {
            var originatingAssembly = GetOriginatingAssembly();
            if (originatingAssembly is null)
            {
                return null;
            }

            var attribute = originatingAssembly.GetCustomAttribute<SyncSourceTrackingHeaderAttribute>();
            return attribute is null ? null : ComposeSourceHeaderValue(originatingAssembly, attribute);
        }
        catch
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Assembly? GetOriginatingAssembly() => SdkTrackingHeaders.FindOriginatingAssembly(SdkAssembly);
}
