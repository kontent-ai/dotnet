using AwesomeAssertions;
using Kontent.Ai.Sync.Configuration;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class SyncOptionsBuilderTests
{
    [Fact]
    public void Build_ReturnsNewInstance_EachTime()
    {
        var builder = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId("test-environment-id");

        var options1 = builder.Build();
        var options2 = builder.Build();

        options1.Should().NotBeSameAs(options2, "Build() should return a new instance each time");
        options1.EnvironmentId.Should().Be(options2.EnvironmentId);
    }

    [Fact]
    public void WithEnvironmentId_String_SetsEnvironmentId()
    {
        var environmentId = "test-environment-id";

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(environmentId)
            .Build();

        options.EnvironmentId.Should().Be(environmentId);
    }

    [Fact]
    public void WithEnvironmentId_Guid_SetsEnvironmentId()
    {
        var environmentId = Guid.NewGuid();

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(environmentId)
            .Build();

        options.EnvironmentId.Should().Be(environmentId.ToString());
    }

    [Fact]
    public void UseProductionApi_ConfiguresProductionMode()
    {
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UseProductionApi()
            .Build();

        options.ApiMode.Should().Be(ApiMode.Public);
    }

    [Fact]
    public void UsePreviewApi_ConfiguresPreviewMode()
    {
        var apiKey = "preview-api-key";

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UsePreviewApi(apiKey)
            .Build();

        options.ApiMode.Should().Be(ApiMode.Preview);
        options.ApiKey.Should().Be(apiKey);
    }

    [Fact]
    public void UseProductionApi_WithSecureAccessKey_ConfiguresSecureMode()
    {
        var apiKey = "delivery-api-key";

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UseProductionApi(apiKey)
            .Build();

        options.ApiMode.Should().Be(ApiMode.Secure);
        options.ApiKey.Should().Be(apiKey);
    }

    [Fact]
    [Obsolete("Covers the obsolete UseSecureApi until it is removed in 3.0.")]
    public void UseSecureApi_ConfiguresSecureMode_LikeUseProductionApi()
    {
        var apiKey = "delivery-api-key";

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UseSecureApi(apiKey)
            .Build();

        options.ApiMode.Should().Be(ApiMode.Secure);
        options.ApiKey.Should().Be(apiKey);
    }

    [Fact]
    public void WithTimeout_SetsTheCeiling()
    {
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .WithTimeout(TimeSpan.FromMinutes(5))
            .Build();

        options.Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void WithoutTimeout_LeavesTheCeilingToTheSdk()
    {
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .Build();

        options.Timeout.Should().BeNull();
    }

    [Fact]
    public void DisableRetryPolicy_DisablesResilience()
    {
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .DisableRetryPolicy()
            .Build();

        options.EnableResilience.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://custom.endpoint.com")]
    [InlineData("https://localhost:5001")]
    public void WithCustomEndpoint_String_SetsCustomEndpoint(string endpoint)
    {
        // Act - Production mode
        var productionOptions = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UseProductionApi()
            .WithCustomEndpoint(endpoint)
            .Build();

        productionOptions.ProductionEndpoint.Should().Be(endpoint);
        productionOptions.PreviewEndpoint.Should().Be(endpoint);

        // Act - Preview mode
        var previewOptions = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UsePreviewApi("test-key")
            .WithCustomEndpoint(endpoint)
            .Build();

        previewOptions.PreviewEndpoint.Should().Be(endpoint);
        previewOptions.ProductionEndpoint.Should().Be(endpoint);
    }

    [Fact]
    public void WithCustomEndpoint_Uri_SetsCustomEndpoint()
    {
        var endpoint = new Uri("https://custom.endpoint.com");

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .WithCustomEndpoint(endpoint)
            .Build();

        options.ProductionEndpoint.Should().Be(endpoint.AbsoluteUri);
        options.PreviewEndpoint.Should().Be(endpoint.AbsoluteUri);
    }

    [Fact]
    public void FluentInterface_AllowsMethodChaining()
    {
        var environmentId = Guid.NewGuid();
        var apiKey = "test-api-key";
        var customEndpoint = "https://custom.endpoint.com";

        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(environmentId)
            .UsePreviewApi(apiKey)
            .WithCustomEndpoint(customEndpoint)
            .DisableRetryPolicy()
            .Build();

        options.EnvironmentId.Should().Be(environmentId.ToString());
        options.ApiMode.Should().Be(ApiMode.Preview);
        options.ApiKey.Should().Be(apiKey);
        options.PreviewEndpoint.Should().Be(customEndpoint);
        options.ProductionEndpoint.Should().Be(customEndpoint);
        options.EnableResilience.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithEnvironmentId_NullOrWhitespace_Throws(string? environmentId)
    {
        var builder = SyncOptionsBuilder.CreateInstance();

        var act = () => builder.WithEnvironmentId(environmentId!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UsePreviewApi_NullOrWhitespaceKey_Throws(string? apiKey)
    {
        var builder = SyncOptionsBuilder.CreateInstance();

        var act = () => builder.UsePreviewApi(apiKey!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseProductionApi_NullOrWhitespaceSecureAccessKey_Throws(string? apiKey)
    {
        var builder = SyncOptionsBuilder.CreateInstance();

        var act = () => builder.UseProductionApi(apiKey!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithCustomEndpoint_NullOrWhitespace_Throws(string? endpoint)
    {
        var builder = SyncOptionsBuilder.CreateInstance();

        var act = () => builder.WithCustomEndpoint(endpoint!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_DefaultValues_AreCorrect()
    {
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .Build();

        options.ApiMode.Should().Be(ApiMode.Public, "default should be public production mode");
        options.EnableResilience.Should().BeTrue("resilience should be enabled by default");
        options.ProductionEndpoint.Should().Be("https://deliver.kontent.ai");
        options.PreviewEndpoint.Should().Be("https://preview-deliver.kontent.ai");
    }
}
