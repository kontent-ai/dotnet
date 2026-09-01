using AwesomeAssertions;
using Kontent.Ai.Sync.Handlers;

namespace Kontent.Ai.Sync.Tests.Handlers;

public class TrackingHandlerTests
{
    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_WithoutPrerelease()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute("Acme.Tool", 2, 3, 4);

        var value = TrackingHandler.ComposeSourceHeaderValue(typeof(TrackingHandler).Assembly, attribute);

        value.Should().Be("Acme.Tool;2.3.4");
    }

    [Fact]
    public void ComposeSourceHeaderValue_ExplicitVersion_WithoutPackageName_FallsBackToTheAssemblyName()
    {
        var attribute = new SyncSourceTrackingHeaderAttribute(null!, 2, 3, 4);

        var value = TrackingHandler.ComposeSourceHeaderValue(typeof(TrackingHandler).Assembly, attribute);

        value.Should().Be("Kontent.Ai.Sync;2.3.4");
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
    public void GetOriginatingAssembly_CalledFromTestAssembly_ReturnsTestAssembly()
    {
        var originating = TrackingHandler.GetOriginatingAssembly();

        originating.Should().NotBeNull();
        originating.GetName().Name.Should().Be(typeof(TrackingHandlerTests).Assembly.GetName().Name);
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
