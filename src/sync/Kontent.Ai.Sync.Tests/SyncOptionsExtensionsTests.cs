using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Tests;

public class SyncOptionsExtensionsTests
{
    [Fact]
    public void GetBaseUrl_NullOptions_ThrowsArgumentNullException()
    {
        SyncOptions? options = null;

        Action act = () => options!.GetBaseUrl();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void GetApiKey_NullOptions_ThrowsArgumentNullException()
    {
        SyncOptions? options = null;

        Action act = () => options!.GetApiKey();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void GetBaseUrl_PreviewMode_ReturnsPreviewEndpoint()
    {
        var options = new SyncOptions
        {
            ApiMode = ApiMode.Preview,
            ProductionEndpoint = "https://deliver.kontent.ai",
            PreviewEndpoint = "https://preview-deliver.kontent.ai"
        };

        var baseUrl = options.GetBaseUrl();

        baseUrl.Should().Be(options.PreviewEndpoint);
    }

    [Theory]
    [InlineData(ApiMode.Public)]
    [InlineData(ApiMode.Secure)]
    public void GetBaseUrl_NonPreviewMode_ReturnsProductionEndpoint(ApiMode mode)
    {
        var options = new SyncOptions
        {
            ApiMode = mode,
            ProductionEndpoint = "https://deliver.kontent.ai",
            PreviewEndpoint = "https://preview-deliver.kontent.ai"
        };

        var baseUrl = options.GetBaseUrl();

        baseUrl.Should().Be(options.ProductionEndpoint);
    }

    [Fact]
    public void GetApiKey_PublicMode_ReturnsNull()
    {
        var options = new SyncOptions
        {
            ApiMode = ApiMode.Public,
            ApiKey = "should-not-be-used"
        };

        var apiKey = options.GetApiKey();

        apiKey.Should().BeNull();
    }

    [Fact]
    public void GetApiKey_PreviewMode_ReturnsApiKey()
    {
        var options = new SyncOptions
        {
            ApiMode = ApiMode.Preview,
            ApiKey = "preview-key"
        };

        var apiKey = options.GetApiKey();

        apiKey.Should().Be("preview-key");
    }

    [Fact]
    public void GetApiKey_SecureMode_ReturnsApiKey()
    {
        var options = new SyncOptions
        {
            ApiMode = ApiMode.Secure,
            ApiKey = "secure-key"
        };

        var apiKey = options.GetApiKey();

        apiKey.Should().Be("secure-key");
    }
}
