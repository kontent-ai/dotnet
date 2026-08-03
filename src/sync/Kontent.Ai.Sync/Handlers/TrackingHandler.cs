using System.Diagnostics;
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
    private const string PackageRepositoryHost = "nuget.org";

    private static readonly Assembly SdkAssembly = typeof(TrackingHandler).Assembly;
    private static readonly string SdkVersion = GetAssemblyVersion(SdkAssembly);
    private static readonly string SdkId = $"{PackageRepositoryHost};{SdkAssembly.GetName().Name};{SdkVersion}";
    private static readonly Lazy<string?> LazySource = new(ResolveSourceHeaderValue);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(SdkIdHeaderName);
        request.Headers.Add(SdkIdHeaderName, SdkId);

        var source = LazySource.Value;
        request.Headers.Remove(SourceHeaderName);
        if (source is not null)
        {
            request.Headers.Add(SourceHeaderName, source);
        }

        if (logger is not null)
        {
            LoggerMessages.HttpTrackingHeadersAdded(logger, SdkId);
        }

        return base.SendAsync(request, cancellationToken);
    }

    internal static string GetAssemblyVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        return StripBuildMetadata(version);
    }

    internal static string StripBuildMetadata(string version)
    {
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
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
            version = GetAssemblyVersion(originatingAssembly);
        }
        else
        {
            packageName = attribute.PackageName;
            var preRelease = string.IsNullOrEmpty(attribute.PreReleaseLabel) ? "" : $"-{attribute.PreReleaseLabel}";
            version = $"{attribute.MajorVersion}.{attribute.MinorVersion}.{attribute.PatchVersion}{preRelease}";
        }

        return $"{packageName};{version}";
    }

    private static string? ResolveSourceHeaderValue()
    {
        var originatingAssembly = GetOriginatingAssembly();
        if (originatingAssembly is null)
        {
            return null;
        }

        var attribute = originatingAssembly.GetCustomAttribute<SyncSourceTrackingHeaderAttribute>();
        return attribute is null ? null : ComposeSourceHeaderValue(originatingAssembly, attribute);
    }

    internal static Assembly? GetOriginatingAssembly()
    {
        var sdkFullName = SdkAssembly.FullName;
        if (sdkFullName is null)
        {
            return null;
        }

        Assembly? deepestCaller = null;
        var frames = new StackTrace().GetFrames();
        foreach (var frame in frames)
        {
            var assembly = frame.GetMethod()?.ReflectedType?.Assembly;
            if (assembly is null || assembly == SdkAssembly)
            {
                continue;
            }

            if (assembly.GetReferencedAssemblies().Any(name => name.FullName == sdkFullName))
            {
                deepestCaller = assembly;
            }
        }

        return deepestCaller;
    }
}
