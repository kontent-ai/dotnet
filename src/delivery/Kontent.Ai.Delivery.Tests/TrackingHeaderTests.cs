using Kontent.Ai.Common.Http;
using Kontent.Ai.Delivery.Extensions;

namespace Kontent.Ai.Delivery.Tests;

public class TrackingHeaderTests
{
    [Fact]
    public void SourceTrackingHeaderGeneratedFromAttribute()
    {
        var attr = new DeliverySourceTrackingHeaderAttribute("CustomModule", 1, 2, 3);

        var value = HttpRequestHeadersExtensions.GenerateSourceTrackingHeaderValue(GetType().Assembly, attr);

        Assert.Equal("CustomModule;1.2.3", value);
    }

    [Fact]
    public void GenerateSourceTrackingHeaderValue_ExplicitVersion_WithoutPackageName_FallsBackToTheAssemblyName()
    {
        var attr = new DeliverySourceTrackingHeaderAttribute(null!, 2, 3, 4);

        var value = HttpRequestHeadersExtensions.GenerateSourceTrackingHeaderValue(GetType().Assembly, attr);

        Assert.Equal($"{GetType().Assembly.GetName().Name};2.3.4", value);
    }

    [Fact]
    public void SourceTrackingHeaderGeneratedFromAssemblyWithCustomPackageName()
    {
        var assembly = GetType().Assembly;
        var sourceVersion = assembly.GetProductVersion();
        var attr = new DeliverySourceTrackingHeaderAttribute("Acme.CustomPlugin");

        var value = HttpRequestHeadersExtensions.GenerateSourceTrackingHeaderValue(assembly, attr);

        Assert.Equal($"Acme.CustomPlugin;{sourceVersion}", value);
    }

    [Fact]
    public void SourceTrackingHeaderGeneratedFromAssembly()
    {
        var assembly = GetType().Assembly;
        var sourceVersion = assembly.GetProductVersion();
        var attr = new DeliverySourceTrackingHeaderAttribute();

        var value = HttpRequestHeadersExtensions.GenerateSourceTrackingHeaderValue(assembly, attr);

        Assert.Equal($"Kontent.Ai.Delivery.Tests;{sourceVersion}", value);
    }

    [Fact]
    public void SourceGeneratedCorrectly()
    {
        var sourceAssembly = HttpRequestHeadersExtensions.GetOriginatingAssembly();
        var sourceVersion = sourceAssembly!.GetProductVersion();

        var source = HttpRequestHeadersExtensions.GetSource();

        Assert.Equal($"Kontent.Ai.Delivery.Tests;{sourceVersion}", source);
    }

    [Fact]
    public void SourceTrackingHeaderWithPreReleaseLabel()
    {
        var attr = new DeliverySourceTrackingHeaderAttribute("CustomModule", 2, 0, 0, "beta.1");

        var value = HttpRequestHeadersExtensions.GenerateSourceTrackingHeaderValue(GetType().Assembly, attr);

        Assert.Equal("CustomModule;2.0.0-beta.1", value);
    }

    [Fact]
    public void SourceTrackingHeaderWithEmptyPreReleaseLabel_OmitsTheHyphen()
    {
        // An empty label is not a prerelease; emitting the separator anyway ships "2.0.0-", which is not
        // a valid SemVer version.
        var attr = new DeliverySourceTrackingHeaderAttribute("CustomModule", 2, 0, 0, "");

        var value = HttpRequestHeadersExtensions.GenerateSourceTrackingHeaderValue(GetType().Assembly, attr);

        Assert.Equal("CustomModule;2.0.0", value);
    }

    // A retried request re-dispatches the same HttpRequestMessage, so the handler runs again on headers
    // it already wrote. A plain Add would send one duplicate X-KC-SDKID per attempt.
    [Fact]
    public void AddSdkTrackingHeader_CalledTwice_WritesSingleValue()
    {
        using var request = new HttpRequestMessage();

        request.Headers.AddSdkTrackingHeader();
        request.Headers.AddSdkTrackingHeader();

        Assert.Single(request.Headers.GetValues("X-KC-SDKID"));
    }

    [Fact]
    public void AddSourceTrackingHeader_CalledTwice_WritesSingleValue()
    {
        using var request = new HttpRequestMessage();

        request.Headers.AddSourceTrackingHeader();
        request.Headers.AddSourceTrackingHeader();

        Assert.Single(request.Headers.GetValues("X-KC-SOURCE"));
    }

    [Fact]
    public void GetProductVersion_ReturnsNonEmptyVersion()
    {
        var assembly = GetType().Assembly;

        var version = assembly.GetProductVersion();

        Assert.False(string.IsNullOrEmpty(version));
    }

    [Fact]
    public void GetSdkVersion_ReturnsNonEmptyString()
    {
        var version = HttpRequestHeadersExtensions.GetSdkVersion();

        Assert.False(string.IsNullOrEmpty(version));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("1.2.3+abc1234", "1.2.3")]
    [InlineData("1.2.3-rc.1+abc1234", "1.2.3-rc.1")]
    [InlineData("5.0.0-beta.2+sha.githash.20260416", "5.0.0-beta.2")]
    public void StripBuildMetadata_RemovesPlusSuffixPreservesPrerelease(string input, string expected)
    {
        Assert.Equal(expected, SdkTrackingHeaders.StripBuildMetadata(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StripBuildMetadata_NullOrWhitespace_ReturnsNull(string? input)
    {
        Assert.Null(SdkTrackingHeaders.StripBuildMetadata(input));
    }

    [Fact]
    public void GetProductVersion_DoesNotContainBuildMetadata()
    {
        var version = GetType().Assembly.GetProductVersion();

        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public void GetSdkVersion_DoesNotContainBuildMetadata()
    {
        var header = HttpRequestHeadersExtensions.GetSdkVersion();

        Assert.DoesNotContain('+', header);
    }
}
