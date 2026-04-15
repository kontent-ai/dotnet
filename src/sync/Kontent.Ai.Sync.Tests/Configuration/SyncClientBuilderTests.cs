using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

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
    public void ConfigureServices_Null_Throws()
    {
        var builder = SyncClientBuilder.WithOptions(o => o
            .WithEnvironmentId(EnvironmentId)
            .UseProductionApi()
            .Build());

        var act = () => builder.ConfigureServices(null!);

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

        var builder = (SyncClientBuilder)ctor!.Invoke(null);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithOptions*");
    }

    [Fact]
    public void Build_DisposingClient_DisposesServiceProvider()
    {
        var sentinel = new DisposableSentinel();

        var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .ConfigureServices(services => RegisterSentinelForDisposal(services, sentinel))
            .Build();

        sentinel.Disposed.Should().BeFalse();

        client.Dispose();

        sentinel.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Build_AwaitUsing_DisposesProvider()
    {
        var sentinel = new DisposableSentinel();

        await using (var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .ConfigureServices(services => RegisterSentinelForDisposal(services, sentinel))
            .Build())
        {
            sentinel.Disposed.Should().BeFalse();
        }

        sentinel.Disposed.Should().BeTrue();
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
        var sentinel1 = new DisposableSentinel();
        var sentinel2 = new DisposableSentinel();

        var builder1 = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .ConfigureServices(services => RegisterSentinelForDisposal(services, sentinel1));

        var builder2 = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .ConfigureServices(services => RegisterSentinelForDisposal(services, sentinel2));

        var client1 = builder1.Build();
        var client2 = builder2.Build();

        client1.Should().NotBeSameAs(client2);

        client1.Dispose();
        sentinel1.Disposed.Should().BeTrue();
        sentinel2.Disposed.Should().BeFalse("disposing one client must not affect another");

        client2.Dispose();
        sentinel2.Disposed.Should().BeTrue();
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
    public void ConfigureServices_MultipleCalls_ExecuteInRegistrationOrder()
    {
        var calls = new List<int>();

        using var client = SyncClientBuilder
            .WithOptions(o => o
                .WithEnvironmentId(EnvironmentId)
                .UseProductionApi()
                .Build())
            .ConfigureServices(_ => calls.Add(1))
            .ConfigureServices(_ => calls.Add(2))
            .ConfigureServices(_ => calls.Add(3))
            .Build();

        calls.Should().Equal(1, 2, 3);
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
        var afterConfigure = builder.ConfigureServices(_ => { });

        afterLogger.Should().BeSameAs(builder);
        afterConfigure.Should().BeSameAs(builder);
    }

    /// <summary>
    /// Registers a sentinel so that the DI container owns it (factory delegate) AND so it is
    /// eagerly resolved (via <see cref="IConfigureOptions{TOptions}"/>, which the options pipeline
    /// invokes the first time <see cref="SyncOptions"/> is materialized — which happens when
    /// <see cref="ISyncClient"/> is resolved by the builder's <c>Build()</c>).
    /// Pre-built singletons passed via <c>AddSingleton(instance)</c> are NOT disposed by the
    /// container, and lazy singletons that are never resolved are never created (and therefore
    /// never disposed). This helper avoids both pitfalls.
    /// </summary>
    private static void RegisterSentinelForDisposal(IServiceCollection services, DisposableSentinel sentinel)
    {
        services.AddSingleton<IConfigureOptions<SyncOptions>>(_ => sentinel);
    }

    private sealed class DisposableSentinel : IConfigureOptions<SyncOptions>, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Configure(SyncOptions options) { }
        public void Dispose() => Disposed = true;
    }
}
