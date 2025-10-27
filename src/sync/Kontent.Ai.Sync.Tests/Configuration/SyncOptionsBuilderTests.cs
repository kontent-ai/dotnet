using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Configuration;
using Xunit;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class SyncOptionsBuilderTests
{
    [Fact]
    public void Build_ReturnsNewInstance_EachTime()
    {
        // Arrange
        var builder = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId("test-environment-id");

        // Act
        var options1 = builder.Build();
        var options2 = builder.Build();

        // Assert
        options1.Should().NotBeSameAs(options2, "Build() should return a new instance each time");
        options1.EnvironmentId.Should().Be(options2.EnvironmentId);
    }

    [Fact]
    public void WithEnvironmentId_String_SetsEnvironmentId()
    {
        // Arrange
        var environmentId = "test-environment-id";

        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(environmentId)
            .Build();

        // Assert
        options.EnvironmentId.Should().Be(environmentId);
    }

    [Fact]
    public void WithEnvironmentId_Guid_SetsEnvironmentId()
    {
        // Arrange
        var environmentId = Guid.NewGuid();

        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(environmentId)
            .Build();

        // Assert
        options.EnvironmentId.Should().Be(environmentId.ToString());
    }

    [Fact]
    public void UseProductionApi_ConfiguresProductionMode()
    {
        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UseProductionApi()
            .Build();

        // Assert
        options.UsePreviewApi.Should().BeFalse();
    }

    [Fact]
    public void UsePreviewApi_ConfiguresPreviewMode()
    {
        // Arrange
        var apiKey = "preview-api-key";

        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UsePreviewApi(apiKey)
            .Build();

        // Assert
        options.UsePreviewApi.Should().BeTrue();
        options.PreviewApiKey.Should().Be(apiKey);
    }

    [Fact]
    public void DisableRetryPolicy_DisablesResilience()
    {
        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .DisableRetryPolicy()
            .Build();

        // Assert
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

        // Assert
        productionOptions.ProductionEndpoint.Should().Be(endpoint);

        // Act - Preview mode
        var previewOptions = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .UsePreviewApi("test-key")
            .WithCustomEndpoint(endpoint)
            .Build();

        // Assert
        previewOptions.PreviewEndpoint.Should().Be(endpoint);
    }

    [Fact]
    public void WithCustomEndpoint_Uri_SetsCustomEndpoint()
    {
        // Arrange
        var endpoint = new Uri("https://custom.endpoint.com");

        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .WithCustomEndpoint(endpoint)
            .Build();

        // Assert
        options.ProductionEndpoint.Should().Be(endpoint.AbsoluteUri);
    }

    [Fact]
    public void FluentInterface_AllowsMethodChaining()
    {
        // Arrange
        var environmentId = Guid.NewGuid();
        var apiKey = "test-api-key";
        var customEndpoint = "https://custom.endpoint.com";

        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(environmentId)
            .UsePreviewApi(apiKey)
            .WithCustomEndpoint(customEndpoint)
            .DisableRetryPolicy()
            .Build();

        // Assert
        options.EnvironmentId.Should().Be(environmentId.ToString());
        options.UsePreviewApi.Should().BeTrue();
        options.PreviewApiKey.Should().Be(apiKey);
        options.PreviewEndpoint.Should().Be(customEndpoint);
        options.EnableResilience.Should().BeFalse();
    }

    [Fact]
    public void Build_DefaultValues_AreCorrect()
    {
        // Act
        var options = SyncOptionsBuilder.CreateInstance()
            .WithEnvironmentId(Guid.NewGuid())
            .Build();

        // Assert
        options.UsePreviewApi.Should().BeFalse("default should be production mode");
        options.EnableResilience.Should().BeTrue("resilience should be enabled by default");
        options.ProductionEndpoint.Should().Be("https://deliver.kontent.ai");
        options.PreviewEndpoint.Should().Be("https://preview-deliver.kontent.ai");
    }
}
