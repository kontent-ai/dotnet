// Shared source, compiled into each SDK assembly - see src/common/README.md.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Builds and writes the analytics headers every Kontent.ai SDK sends, per
/// https://kontent-ai.github.io/articles/Guidelines-for-Kontent.ai-related-tools.html#analytics.
/// </summary>
/// <remarks>
/// Reading the source-tracking attribute stays with each SDK: that attribute is part of the product's
/// public surface and there is one per product. Each SDK binds its own assembly and its own attribute
/// type; everything else lives here.
/// </remarks>
internal static class SdkTrackingHeaders
{
    private const string SdkHeaderName = "X-KC-SDKID";
    private const string SourceHeaderName = "X-KC-SOURCE";
    private const string PackageRepositoryHost = "nuget.org";

    internal static string ComposeSdkHeaderValue(Assembly sdkAssembly)
        => $"{PackageRepositoryHost};{sdkAssembly.GetName().Name};{sdkAssembly.GetProductVersion()}";

    // Resilience retries re-dispatch the same HttpRequestMessage instance, so header writes must be
    // idempotent - a plain Add accumulates one duplicate value per attempt.
    internal static void SetSdkHeader(this HttpRequestHeaders headers, string value)
    {
        headers.Remove(SdkHeaderName);
        headers.Add(SdkHeaderName, value);
    }

    internal static void SetSourceHeader(this HttpRequestHeaders headers, string? value)
    {
        if (value is null)
        {
            return;
        }

        headers.Remove(SourceHeaderName);
        headers.Add(SourceHeaderName, value);
    }

    /// <summary>
    /// Reads the SemVer informational version and drops the build-metadata suffix ('+...'), which
    /// deterministic / SourceLink builds append as the commit hash - it does not belong in a tracking
    /// header. Pre-release suffixes ('-rc.1') are kept. Falls back to "0.0.0" for unversioned builds.
    /// </summary>
    internal static string GetProductVersion(this Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return StripBuildMetadata(informationalVersion) ?? "0.0.0";
    }

    internal static string? StripBuildMetadata(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex < 0 ? version : version[..plusIndex];
    }

    /// <summary>
    /// Best-effort: walks the synchronous call stack for the outermost assembly referencing
    /// <paramref name="sdkAssembly"/>, so a package built on an SDK can be attributed via its
    /// source-tracking attribute. Returns null when no such frame is present (for instance when first
    /// invoked from an async continuation) - the source header is then simply omitted.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Assembly? FindOriginatingAssembly(Assembly sdkAssembly)
        => new StackTrace().GetFrames()
            .Select(frame => frame.GetMethod()?.ReflectedType?.Assembly)
            .Distinct()
            .OfType<Assembly>()
            .LastOrDefault(assembly => assembly
                .GetReferencedAssemblies()
                .Any(referenced => referenced.FullName == sdkAssembly.FullName));
}
