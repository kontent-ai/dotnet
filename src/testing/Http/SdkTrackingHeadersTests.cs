// Shared test source, compiled into each test assembly - see src/testing/README.md.

using AwesomeAssertions;
using Kontent.Ai.Common.Http;

namespace Kontent.Ai.Testing.Http;

/// <summary>
/// Pins <see cref="SdkTrackingHeaders"/> in every product that compiles it. The SDK assembly under test is
/// the one this file was compiled into; the product-specific half - reading the product's own
/// source-tracking attribute - stays in each product's tests.
/// </summary>
public class SdkTrackingHeadersTests
{
    private static System.Reflection.Assembly SdkAssembly => typeof(SdkTrackingHeaders).Assembly;

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("2.0.0-beta", "2.0.0-beta")]
    [InlineData("1.0.0+abc123", "1.0.0")]
    [InlineData("1.2.3-rc.1+abc1234", "1.2.3-rc.1")]
    [InlineData("1.2.3-rc.1+cb8ea2a2edf788814cb009f470e877bd94a6af00", "1.2.3-rc.1")]
    [InlineData("5.0.0-beta.2+sha.githash.20260416", "5.0.0-beta.2")]
    public void StripBuildMetadata_RemovesThePlusSuffixAndKeepsThePrerelease(string raw, string expected)
    {
        SdkTrackingHeaders.StripBuildMetadata(raw).Should().Be(expected);
    }

    // A blank version yields null so the caller falls back to "0.0.0"; an empty string would otherwise
    // travel into the tracking header as the version.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StripBuildMetadata_NullOrWhitespace_ReturnsNull(string? raw)
    {
        SdkTrackingHeaders.StripBuildMetadata(raw).Should().BeNull();
    }

    // SourceLink appends "+<commit-sha>" to AssemblyInformationalVersion; the tracking header must
    // never leak that SHA.
    [Fact]
    public void GetProductVersion_SdkAssembly_IsSetAndCarriesNoBuildMetadata()
    {
        var version = SdkAssembly.GetProductVersion();

        version.Should().NotBeNullOrWhiteSpace();
        version.Should().NotContain("+");
    }

    [Fact]
    public void ComposeSdkHeaderValue_NamesTheRepositoryPackageAndStrippedVersion()
    {
        SdkTrackingHeaders.ComposeSdkHeaderValue(SdkAssembly)
            .Should().Be($"nuget.org;{SdkAssembly.GetName().Name};{SdkAssembly.GetProductVersion()}");
    }

    [Theory]
    [InlineData(null, "2.0.0")]
    // An empty label is not a prerelease; emitting the separator anyway ships "2.0.0-", which is not a
    // valid SemVer version.
    [InlineData("", "2.0.0")]
    [InlineData("beta.1", "2.0.0-beta.1")]
    public void FormatSourceVersion_AppendsThePreReleaseLabelOnlyWhenThereIsOne(string? label, string expected)
    {
        SdkTrackingHeaders.FormatSourceVersion(2, 0, 0, label).Should().Be(expected);
    }

    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_NamesThePackage()
    {
        SdkTrackingHeaders.ComposeSourceHeaderValue(SdkAssembly, "Acme.Tool", 2, 3, 4, null).Should().Be("Acme.Tool;2.3.4");
    }

    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_WithoutPackageName_FallsBackToTheAssemblyName()
    {
        SdkTrackingHeaders.ComposeSourceHeaderValue(SdkAssembly, null, 2, 3, 4, null)
            .Should().Be($"{SdkAssembly.GetName().Name};2.3.4");
    }

    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_WithPrerelease()
    {
        SdkTrackingHeaders.ComposeSourceHeaderValue(SdkAssembly, "Acme.Tool", 1, 0, 0, "rc1").Should().Be("Acme.Tool;1.0.0-rc1");
    }

    [Fact]
    public void ComposeSourceHeaderValue_FromAssembly_UsesTheAssemblyNameAndStrippedVersion()
    {
        SdkTrackingHeaders.ComposeSourceHeaderValue(SdkAssembly, packageName: null)
            .Should().Be($"{SdkAssembly.GetName().Name};{SdkAssembly.GetProductVersion()}");
    }

    [Fact]
    public void ComposeSourceHeaderValue_FromAssembly_WithPackageName_OverridesTheName()
    {
        SdkTrackingHeaders.ComposeSourceHeaderValue(SdkAssembly, "Custom.Package")
            .Should().StartWith("Custom.Package;");
    }

    // A retried request re-dispatches the same HttpRequestMessage, so the handler runs again on headers
    // it already wrote. A plain Add would send one duplicate per attempt.
    [Fact]
    public void SetSdkHeader_CalledTwice_WritesSingleValue()
    {
        using var request = new HttpRequestMessage();

        request.Headers.SetSdkHeader("nuget.org;Sdk;1.0.0");
        request.Headers.SetSdkHeader("nuget.org;Sdk;1.0.0");

        request.Headers.GetValues("X-KC-SDKID").Should().ContainSingle();
    }

    [Fact]
    public void SetSourceHeader_CalledTwice_WritesSingleValue()
    {
        using var request = new HttpRequestMessage();

        request.Headers.SetSourceHeader("Acme.Tool;1.0.0");
        request.Headers.SetSourceHeader("Acme.Tool;1.0.0");

        request.Headers.GetValues("X-KC-SOURCE").Should().ContainSingle();
    }

    [Fact]
    public void SetSourceHeader_Null_WritesNothing()
    {
        using var request = new HttpRequestMessage();

        request.Headers.SetSourceHeader(null);

        request.Headers.Contains("X-KC-SOURCE").Should().BeFalse();
    }

    [Fact]
    public void FindOriginatingAssembly_CalledFromTheTestAssembly_ReturnsIt()
    {
        var originating = SdkTrackingHeaders.FindOriginatingAssembly(SdkAssembly);

        originating.Should().NotBeNull();
        originating.GetName().Name.Should().Be(typeof(SdkTrackingHeadersTests).Assembly.GetName().Name);
    }
}
