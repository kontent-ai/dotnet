using AwesomeAssertions;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class SyncOptionsExtensionsTests
{
    [Fact]
    public void Defaults_ArePublicProductionWithResilience()
    {
        var options = new SyncOptions();

        options.ApiMode.Should().Be(ApiMode.Public);
        options.EnableResilience.Should().BeTrue();
        options.Timeout.Should().BeNull();
        options.ProductionEndpoint.Should().Be("https://deliver.kontent.ai");
        options.PreviewEndpoint.Should().Be("https://preview-deliver.kontent.ai");
    }

    [Fact]
    public void UseProductionApi_ConfiguresPublicMode()
    {
        var options = new SyncOptions().UsePreviewApi("key").UseProductionApi();

        options.ApiMode.Should().Be(ApiMode.Public);
    }

    [Fact]
    public void UsePreviewApi_ConfiguresPreviewMode()
    {
        var options = new SyncOptions().UsePreviewApi("preview-api-key");

        options.ApiMode.Should().Be(ApiMode.Preview);
        options.ApiKey.Should().Be("preview-api-key");
    }

    [Fact]
    public void UseProductionApi_WithSecureAccessKey_ConfiguresSecureMode()
    {
        var options = new SyncOptions().UseProductionApi("delivery-api-key");

        options.ApiMode.Should().Be(ApiMode.Secure);
        options.ApiKey.Should().Be("delivery-api-key");
    }

    [Theory]
    [InlineData("https://custom.endpoint.com")]
    [InlineData("https://localhost:5001")]
    public void UseCustomEndpoint_String_SetsBothEndpoints(string endpoint)
    {
        var options = new SyncOptions().UseCustomEndpoint(endpoint);

        options.ProductionEndpoint.Should().Be(endpoint);
        options.PreviewEndpoint.Should().Be(endpoint);
    }

    [Fact]
    public void UseCustomEndpoint_Uri_SetsBothEndpoints()
    {
        var endpoint = new Uri("https://custom.endpoint.com");

        var options = new SyncOptions().UseCustomEndpoint(endpoint);

        options.ProductionEndpoint.Should().Be(endpoint.AbsoluteUri);
        options.PreviewEndpoint.Should().Be(endpoint.AbsoluteUri);
    }

    [Fact]
    public void Extensions_Chain()
    {
        var environmentId = Guid.NewGuid().ToString();

        var options = new SyncOptions { EnvironmentId = environmentId, EnableResilience = false }
            .UsePreviewApi("test-api-key")
            .UseCustomEndpoint("https://custom.endpoint.com");

        options.EnvironmentId.Should().Be(environmentId);
        options.ApiMode.Should().Be(ApiMode.Preview);
        options.ApiKey.Should().Be("test-api-key");
        options.PreviewEndpoint.Should().Be("https://custom.endpoint.com");
        options.ProductionEndpoint.Should().Be("https://custom.endpoint.com");
        options.EnableResilience.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UsePreviewApi_NullOrWhitespaceKey_Throws(string? apiKey)
    {
        var act = () => new SyncOptions().UsePreviewApi(apiKey!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseProductionApi_NullOrWhitespaceSecureAccessKey_Throws(string? apiKey)
    {
        var act = () => new SyncOptions().UseProductionApi(apiKey!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseCustomEndpoint_NullOrWhitespace_Throws(string? endpoint)
    {
        var act = () => new SyncOptions().UseCustomEndpoint(endpoint!);

        act.Should().Throw<ArgumentException>();
    }
}
