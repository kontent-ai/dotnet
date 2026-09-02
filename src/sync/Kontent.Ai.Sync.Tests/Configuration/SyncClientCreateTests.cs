using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class SyncClientCreateTests
{
    private const string EnvironmentId = "550cec62-90a6-4ab3-b3e4-3d0bb4c04f5c";

    [Fact]
    public void Create_NullDelegate_Throws()
    {
        var act = () => SyncClient.Create((Action<ISyncClientBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullOptions_Throws()
    {
        var act = () => SyncClient.Create((SyncOptions)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_ProductionApi_ReturnsClient()
    {
        using var client = SyncClient.Create(sync => sync.Options.Configure(o => o.EnvironmentId = EnvironmentId));

        client.Should().BeAssignableTo<ISyncClient>();
    }

    [Fact]
    public void Create_PreviewApi_ReturnsClient()
    {
        using var client = SyncClient.Create(sync => sync.Options.Configure(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UsePreviewApi("preview.api.key");
        }));

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_SecureApi_ReturnsClient()
    {
        using var client = SyncClient.Create(sync => sync.Options.Configure(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UseProductionApi("secure.api.key");
        }));

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_FromOptionsInstance_CopiesTheValues()
    {
        var options = new SyncOptions { EnvironmentId = EnvironmentId }.UsePreviewApi("preview.api.key");

        using var client = SyncClient.Create(options);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_InvalidOptions_ThrowsOptionsValidationException()
    {
        var act = () => SyncClient.Create(sync => sync.Options.Configure(o => o.EnvironmentId = "not-a-guid"));

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Create_WithLoggerFactory_CreatesClient()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });

        using var client = SyncClient.Create(sync =>
        {
            sync.Services.AddSingleton(loggerFactory);
            sync.Options.Configure(o => o.EnvironmentId = EnvironmentId);
        });

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_MultipleCalls_ProduceIndependentClients()
    {
        Action<ISyncClientBuilder> configure = sync => sync.Options.Configure(o => o.EnvironmentId = EnvironmentId);

        using var client1 = SyncClient.Create(configure);
        using var client2 = SyncClient.Create(configure);

        // Each client owns its own transport, so disposing one must leave the other usable.
        client1.Should().NotBeSameAs(client2);

        client1.Dispose();

        var act = () => client2.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureResilience_ReturnsTheSameBuilder()
    {
        ISyncClientBuilder? seen = null;
        ISyncClientBuilder? returned = null;

        using var client = SyncClient.Create(sync =>
        {
            seen = sync;
            sync.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            returned = sync.ConfigureResilience(_ => { });
        });

        returned.Should().BeSameAs(seen);
    }

    [Fact]
    public void Builder_ExposesTheNameServicesOptionsAndHttpClient()
    {
        using var client = SyncClient.Create(sync =>
        {
            sync.Name.Should().Be("Default");
            sync.Services.Should().NotBeNull();
            sync.Options.Name.Should().Be("Default");
            sync.HttpClient.Name.Should().Be("Kontent.Ai.Sync.HttpClient.Default");
            sync.Options.Configure(o => o.EnvironmentId = EnvironmentId);
        });
    }
}
