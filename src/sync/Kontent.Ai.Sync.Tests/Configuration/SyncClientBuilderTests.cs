using AwesomeAssertions;
using Kontent.Ai.Sync.Configuration;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class SyncClientBuilderTests
{
    private const string EnvironmentId = "550cec62-90a6-4ab3-b3e4-3d0bb4c04f5c";
    private const string TestPreviewApiKey = "preview.api.key";
    private const string TestSecureApiKey = "secure.api.key";

    [Fact]
    public void WithOptions_NullDelegate_Throws()
    {
        var act = () => SyncClientBuilder.WithOptions(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithLoggerFactory_Null_Throws()
    {
        var builder = SyncClientBuilder.WithOptions(o => o
            .WithEnvironmentId(EnvironmentId)
            .UseProductionApi()
            .Build());

        var act = () => builder.WithLoggerFactory(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ProductionApi_ReturnsClient()
    {
        using var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .Build();

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<ISyncClient>();
    }

    [Fact]
    public void Build_PreviewApi_ReturnsClient()
    {
        using var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UsePreviewApi(TestPreviewApiKey)
                .Build())
            .Build();

        client.Should().NotBeNull();
    }

    [Fact]
    public void Build_SecureApi_ReturnsClient()
    {
        using var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseSecureApi(TestSecureApiKey)
                .Build())
            .Build();

        client.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithoutOptions_Throws()
    {
        // Use reflection to bypass WithOptions and verify Build() guards against missing options.
        // Since the constructor is private and WithOptions is the only entry point, this scenario
        // can only be reached if WithOptions is somehow skipped — guard test as a safety net.
        var ctor = typeof(SyncClientBuilder).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            Type.EmptyTypes);
        ctor.Should().NotBeNull("SyncClientBuilder should have a private parameterless constructor");

        var builder = (SyncClientBuilder)ctor.Invoke(null);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithOptions*");
    }

    [Fact]
    public async Task Build_DisposeAsync_IsIdempotent()
    {
        var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .Build();

        await client.DisposeAsync();
        var act = async () => await client.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Build_Dispose_IsIdempotent()
    {
        var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .Build();

        client.Dispose();
        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_MultipleCalls_ProduceIndependentClients()
    {
        var builder = SyncClientBuilder.WithOptions(o => o
            .WithEnvironmentId(EnvironmentId)
            .UseProductionApi()
            .Build());

        using var client1 = builder.Build();
        using var client2 = builder.Build();

        // Each client owns its own HttpClient, so disposing one must leave the other usable.
        client1.Should().NotBeSameAs(client2);

        client1.Dispose();

        var act = () => client2.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithLoggerFactory_CreatesClient()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });

        using var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .WithLoggerFactory(loggerFactory)
            .Build();

        client.Should().NotBeNull();
    }

    [Fact]
    public void FluentChain_ReturnsSameInstance()
    {
        var builder = SyncClientBuilder.WithOptions(o => o
            .WithEnvironmentId(EnvironmentId)
            .UseProductionApi()
            .Build());

        using var loggerFactory = LoggerFactory.Create(_ => { });

        var afterLogger = builder.WithLoggerFactory(loggerFactory);
        var afterResilience = builder.WithResilience(_ => { });

        afterLogger.Should().BeSameAs(builder);
        afterResilience.Should().BeSameAs(builder);
    }
}
