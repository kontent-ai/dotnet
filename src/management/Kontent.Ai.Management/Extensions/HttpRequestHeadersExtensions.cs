using Kontent.Ai.Common.Http;
using Kontent.Ai.Management.Attributes;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Kontent.Ai.Management.Extensions;

internal static class HttpRequestHeadersExtensions
{
    private static readonly Assembly SdkAssembly = typeof(ManagementClient).Assembly;
    private static readonly Lazy<string> Sdk = new(() => SdkTrackingHeaders.ComposeSdkHeaderValue(SdkAssembly));
    private static readonly Lazy<string?> Source = new(GetSource);

    internal static void AddSdkTrackingHeader(this HttpRequestHeaders headers) => headers.SetSdkHeader(Sdk.Value);

    internal static void AddSourceTrackingHeader(this HttpRequestHeaders headers) => headers.SetSourceHeader(Source.Value);

    // Must never throw: the result is cached in a Lazy, so a thrown exception would be cached and rethrown on
    // every subsequent request for the process lifetime - hence the blanket catch; the header is simply omitted.
    private static string? GetSource()
    {
        try
        {
            var originatingAssembly = GetOriginatingAssembly();
            if (originatingAssembly is null)
            {
                return null;
            }

            var attribute = originatingAssembly.GetCustomAttributes<SourceTrackingHeaderAttribute>().FirstOrDefault();
            return attribute is null ? null : GenerateSourceTrackingHeaderValue(originatingAssembly, attribute);
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateSourceTrackingHeaderValue(Assembly originatingAssembly, SourceTrackingHeaderAttribute attribute) =>
        attribute.LoadFromAssembly
            ? SdkTrackingHeaders.ComposeSourceHeaderValue(originatingAssembly, attribute.PackageName)
            : SdkTrackingHeaders.ComposeSourceHeaderValue(
                originatingAssembly,
                attribute.PackageName,
                attribute.MajorVersion,
                attribute.MinorVersion,
                attribute.PatchVersion,
                attribute.PreReleaseLabel);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Assembly? GetOriginatingAssembly() => SdkTrackingHeaders.FindOriginatingAssembly(SdkAssembly);
}
