using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kontent.Ai.Sync.Tests.Extensions;

public class SyncClientFactoryTests
{
    [Fact]
    public void Get_WithRegisteredClient_ReturnsClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act
        var client = factory.Get("production");

        // Assert
        client.Should().NotBeNull();
        client.Should().BeAssignableTo<ISyncClient>();
    }

    [Fact]
    public void Get_WithMultipleRegisteredClients_ReturnsCorrectClient()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        services.AddSyncClient("staging", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
            options.UsePreviewApi = true;
            options.PreviewApiKey = "staging-key";
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act
        var prodClient = factory.Get("production");
        var stagingClient = factory.Get("staging");

        // Assert
        prodClient.Should().NotBeNull();
        stagingClient.Should().NotBeNull();
        prodClient.Should().NotBeSameAs(stagingClient, "different named clients should return different instances");
    }

    [Fact]
    public void Get_WithUnregisteredClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act
        var act = () => factory.Get("nonexistent");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*")
            .WithMessage("*AddSyncClient*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Get_WithInvalidName_ThrowsArgumentException(string? invalidName)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act
        var act = () => factory.Get(invalidName!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Get_CalledMultipleTimes_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act
        var client1 = factory.Get("production");
        var client2 = factory.Get("production");

        // Assert
        client1.Should().BeSameAs(client2, "the same named client should return the same singleton instance");
    }

    [Fact]
    public void Factory_RegisteredOnce_WithMultipleAddSyncClientCalls()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddSyncClient("client1", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        services.AddSyncClient("client2", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var factories = serviceProvider.GetServices<ISyncClientFactory>().ToList();

        // Assert
        factories.Should().HaveCount(1, "factory should only be registered once even with multiple AddSyncClient calls");
    }

    [Fact]
    public void Factory_WorksWithDefaultClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSyncClient(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act
        var client = factory.Get("Default");

        // Assert
        client.Should().NotBeNull();
        client.Should().BeAssignableTo<ISyncClient>();
    }

    [Fact]
    public void Factory_WorksWithBuilderPattern()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        // Act & Assert
        var client = factory.Get("production");
        client.Should().NotBeNull();
    }
}
