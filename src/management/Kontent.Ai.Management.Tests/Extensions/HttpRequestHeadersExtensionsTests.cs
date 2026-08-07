using AwesomeAssertions;
using Kontent.Ai.Common.Http;
using Kontent.Ai.Management.Extensions;

namespace Kontent.Ai.Management.Tests.Extensions;

public class HttpRequestHeadersExtensionsTests
{
    [Fact]
    public void AddSdkTrackingHeader_AddsRepositoryPackageIdAndStrippedVersion()
    {
        var sdkAssembly = typeof(ManagementClient).Assembly;
        var request = new HttpRequestMessage();

        request.Headers.AddSdkTrackingHeader();

        request.Headers.GetValues("X-KC-SDKID").Should()
            .ContainSingle().Which.Should()
            .Be($"nuget.org;{sdkAssembly.GetName().Name};{sdkAssembly.GetProductVersion()}");
    }

    [Fact]
    public void AddSourceTrackingHeader_AddsValueFromConsumingAssemblyAttribute()
    {
        // The test assembly carries [assembly: SourceTrackingHeader] (parameterless) via its .csproj.
        var consumingAssembly = typeof(HttpRequestHeadersExtensionsTests).Assembly;
        var request = new HttpRequestMessage();

        request.Headers.AddSourceTrackingHeader();

        request.Headers.GetValues("X-KC-SOURCE").Should()
            .ContainSingle().Which.Should()
            .Be($"{consumingAssembly.GetName().Name};{consumingAssembly.GetProductVersion()}");
    }

    [Theory]
    [InlineData(null, "2.0.0")]
    // An empty label is not a prerelease; emitting the separator anyway ships "2.0.0-", which is not a
    // valid SemVer version.
    [InlineData("", "2.0.0")]
    [InlineData("beta.1", "2.0.0-beta.1")]
    public void FormatSourceVersion_AppendsThePreReleaseLabelOnlyWhenThereIsOne(string? label, string expected) =>
        SdkTrackingHeaders.FormatSourceVersion(2, 0, 0, label).Should().Be(expected);
}
