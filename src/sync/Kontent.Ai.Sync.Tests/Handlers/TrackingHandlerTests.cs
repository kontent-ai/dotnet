using System.Reflection;
using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Handlers;

namespace Kontent.Ai.Sync.Tests.Handlers;

public class TrackingHandlerTests
{
    [Fact]
    public void GetAssemblyVersion_ReadsInformationalVersion_StrippingBuildMetadata()
    {
        var assembly = typeof(TrackingHandler).Assembly;

        var version = TrackingHandler.GetAssemblyVersion(assembly);

        version.Should().NotBeNullOrWhiteSpace();
        version.Should().NotContain("+");
    }

    [Theory]
    [InlineData("1.2.3-rc.1+cb8ea2a2edf788814cb009f470e877bd94a6af00", "1.2.3-rc.1")]
    [InlineData("1.0.0+abc123", "1.0.0")]
    [InlineData("2.0.0-beta", "2.0.0-beta")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("", "")]
    public void StripBuildMetadata_RemovesSourceLinkGitSha(string raw, string expected)
    {
        TrackingHandler.StripBuildMetadata(raw).Should().Be(expected);
    }

    [Fact]
    public void GetAssemblyVersion_SdkAssembly_NeverContainsPlus()
    {
        // SourceLink appends "+<commit-sha>" to AssemblyInformationalVersion; the tracking header
        // must never leak that SHA. Regression guard against removing StripBuildMetadata.
        var version = TrackingHandler.GetAssemblyVersion(typeof(TrackingHandler).Assembly);

        version.Should().NotContain("+");
    }

    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_WithoutPrerelease()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute("Acme.Tool", 2, 3, 4);

        var value = TrackingHandler.ComposeSourceHeaderValue(typeof(TrackingHandler).Assembly, attribute);

        value.Should().Be("Acme.Tool;2.3.4");
    }

    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_WithPrerelease()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute("Acme.Tool", 1, 0, 0, "rc1");

        var value = TrackingHandler.ComposeSourceHeaderValue(typeof(TrackingHandler).Assembly, attribute);

        value.Should().Be("Acme.Tool;1.0.0-rc1");
    }

    [Fact]
    public void ComposeSourceHeaderValue_LoadFromAssembly_NoPackageNameOverride_UsesAssemblyName()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute();
        var assembly = typeof(TrackingHandler).Assembly;

        var value = TrackingHandler.ComposeSourceHeaderValue(assembly, attribute);

        value.Should().StartWith($"{assembly.GetName().Name};");
        value.Should().NotContain("+");
    }

    [Fact]
    public void ComposeSourceHeaderValue_LoadFromAssembly_WithPackageNameOverride_OverridesName()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute("Custom.Package");
        var assembly = typeof(TrackingHandler).Assembly;

        var value = TrackingHandler.ComposeSourceHeaderValue(assembly, attribute);

        value.Should().StartWith("Custom.Package;");
    }

    [Fact]
    public void ComposeSourceHeaderValue_NullAssembly_Throws()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute();

        var act = () => TrackingHandler.ComposeSourceHeaderValue(null!, attribute);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComposeSourceHeaderValue_NullAttribute_Throws()
    {
        var act = () => TrackingHandler.ComposeSourceHeaderValue(typeof(TrackingHandler).Assembly, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetOriginatingAssembly_CalledFromTestAssembly_ReturnsTestAssembly()
    {
        var originating = TrackingHandler.GetOriginatingAssembly();

        originating.Should().NotBeNull();
        originating!.GetName().Name.Should().Be(typeof(TrackingHandlerTests).Assembly.GetName().Name);
    }

    [Fact]
    public void SyncSourceTrackingHeaderAttribute_DefaultCtor_LoadsFromAssembly()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute();

        attribute.LoadFromAssembly.Should().BeTrue();
        attribute.PackageName.Should().BeNull();
    }

    [Fact]
    public void SyncSourceTrackingHeaderAttribute_PackageNameCtor_LoadsFromAssembly()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute("Acme.Tool");

        attribute.LoadFromAssembly.Should().BeTrue();
        attribute.PackageName.Should().Be("Acme.Tool");
    }

    [Fact]
    public void SyncSourceTrackingHeaderAttribute_FullCtor_DoesNotLoadFromAssembly()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute("Acme.Tool", 2, 3, 4, "beta");

        attribute.LoadFromAssembly.Should().BeFalse();
        attribute.PackageName.Should().Be("Acme.Tool");
        attribute.MajorVersion.Should().Be(2);
        attribute.MinorVersion.Should().Be(3);
        attribute.PatchVersion.Should().Be(4);
        attribute.PreReleaseLabel.Should().Be("beta");
    }
}
