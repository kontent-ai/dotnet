using FluentAssertions;
using Kontent.Ai.Sync;
using Kontent.Ai.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Sync.Tests.Extensions;

public class SyncClientFactoryTests
{
    [Fact]
    public void Get_WithRegisteredNamedClient_ReturnsClient()
    {
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        var client = factory.Get("production");

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<ISyncClient>();
    }

    [Fact]
    public void Get_DefaultClient_ReturnsSameInstanceAsUnkeyedResolution()
    {
        var services = new ServiceCollection();
        services.AddSyncClient(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        var unkeyed = serviceProvider.GetRequiredService<ISyncClient>();
        var fromGetDefault = factory.Get();
        var fromGetByName = factory.Get("Default");

        fromGetDefault.Should().BeSameAs(unkeyed);
        fromGetByName.Should().BeSameAs(unkeyed);
    }

    [Fact]
    public void Get_WithUnregisteredClient_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        Action act = () => factory.Get("nonexistent");

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
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        Action act = () => factory.Get(invalidName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Get_CalledMultipleTimesForSameName_ReturnsSameSingletonInstance()
    {
        var services = new ServiceCollection();
        services.AddSyncClient("production", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISyncClientFactory>();

        var first = factory.Get("production");
        var second = factory.Get("production");

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Factory_IsRegisteredOnlyOnce_WithMultipleClients()
    {
        var services = new ServiceCollection();

        services.AddSyncClient("client1", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        services.AddSyncClient("client2", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        var serviceProvider = services.BuildServiceProvider();
        var factories = serviceProvider.GetServices<ISyncClientFactory>().ToList();

        factories.Should().HaveCount(1);
    }
}
