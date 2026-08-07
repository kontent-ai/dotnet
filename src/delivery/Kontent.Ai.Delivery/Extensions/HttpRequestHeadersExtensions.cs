using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kontent.Ai.Common.Http;

namespace Kontent.Ai.Delivery.Extensions;

internal static class HttpRequestHeadersExtensions
{
    private static readonly Assembly SdkAssembly = typeof(HttpRequestHeadersExtensions).Assembly;
    private static readonly Lazy<string> Sdk = new(() => SdkTrackingHeaders.ComposeSdkHeaderValue(SdkAssembly));
    private static readonly Lazy<string?> Source = new(GetSource);

    public const string WaitForLoadingNewContentHeaderName = "X-KC-Wait-For-Loading-New-Content";

    internal static void AddSdkTrackingHeader(this HttpRequestHeaders headers) => headers.SetSdkHeader(Sdk.Value);

    /// <summary>
    /// Adds a tracking header according to https://kontent-ai.github.io/articles/Guidelines-for-Kontent.ai-related-tools.html#analytics
    /// </summary>
    /// <param name="headers">Collection of headers</param>
    internal static void AddSourceTrackingHeader(this HttpRequestHeaders headers) => headers.SetSourceHeader(Source.Value);

    /// <summary>
    /// Gets the SDK version string for logging purposes.
    /// </summary>
    internal static string GetSdkVersion() => Sdk.Value;

    // Must never throw: the result is cached in a Lazy, so a thrown exception would be cached and rethrown on
    // every subsequent request for the process lifetime - hence the blanket catch; the header is simply omitted.
    internal static string? GetSource()
    {
        try
        {
            var originatingAssembly = GetOriginatingAssembly();
            if (originatingAssembly is null)
            {
                return null;
            }

            var attribute = originatingAssembly.GetCustomAttributes<DeliverySourceTrackingHeaderAttribute>().FirstOrDefault();
            return attribute is null ? null : GenerateSourceTrackingHeaderValue(originatingAssembly, attribute);
        }
        catch
        {
            return null;
        }
    }

    internal static string GenerateSourceTrackingHeaderValue(Assembly originatingAssembly, DeliverySourceTrackingHeaderAttribute attribute)
    {
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
            version = SdkTrackingHeaders.FormatSourceVersion(
                attribute.MajorVersion, attribute.MinorVersion, attribute.PatchVersion, attribute.PreReleaseLabel);
        }
        return $"{packageName};{version}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Assembly? GetOriginatingAssembly() => SdkTrackingHeaders.FindOriginatingAssembly(SdkAssembly);
}
