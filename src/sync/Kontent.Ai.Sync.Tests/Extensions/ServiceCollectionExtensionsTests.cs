using FluentAssertions;
using Kontent.Ai.Sync;
using Kontent.Ai.Sync.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSyncClient_DuplicateName_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSyncClient("production", options => options.EnvironmentId = Guid.NewGuid().ToString());

        Action act = () => services.AddSyncClient("production", options => options.EnvironmentId = Guid.NewGuid().ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been registered*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("name with spaces")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData(null)]
    public void AddSyncClient_InvalidName_ThrowsArgumentException(string? name)
    {
        var services = new ServiceCollection();

        Action act = () => services.AddSyncClient(name!, options => options.EnvironmentId = Guid.NewGuid().ToString());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddSyncClient_DefaultAndNamedClientAccess_IsConsistent()
    {
        var services = new ServiceCollection();

        services.AddSyncClient(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        });

        services.AddSyncClient("preview", options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
            options.ApiMode = ApiMode.Preview;
            options.ApiKey = "preview-key";
        });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISyncClientFactory>();

        var unkeyedDefault = provider.GetRequiredService<ISyncClient>();
        var keyedDefault = provider.GetRequiredKeyedService<ISyncClient>("Default");
        var defaultFromFactory = factory.Get();

        var keyedNamed = provider.GetRequiredKeyedService<ISyncClient>("preview");
        var namedFromFactory = factory.Get("preview");

        unkeyedDefault.Should().BeSameAs(keyedDefault);
        defaultFromFactory.Should().BeSameAs(unkeyedDefault);

        namedFromFactory.Should().BeSameAs(keyedNamed);
        namedFromFactory.Should().NotBeSameAs(unkeyedDefault);
    }

    [Fact]
    public void AddSyncClient_WithConfigurationSection_BindsOptions()
    {
        var environmentId = Guid.NewGuid().ToString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sync:EnvironmentId"] = environmentId,
                ["Sync:ApiMode"] = "Preview",
                ["Sync:ApiKey"] = "preview-key",
                ["Sync:EnableResilience"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSyncClient(configuration.GetSection("Sync"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<SyncOptions>>().CurrentValue;

        options.EnvironmentId.Should().Be(environmentId);
        options.ApiMode.Should().Be(ApiMode.Preview);
        options.ApiKey.Should().Be("preview-key");
        options.EnableResilience.Should().BeFalse();
    }

    [Fact]
    public void AddSyncClient_RuntimeConfigurationChanges_AreReflectedInOptions()
    {
        var environmentId = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddSyncClient("dynamic", options =>
        {
            options.EnvironmentId = environmentId;
            options.ApiMode = ApiMode.Public;
        });

        var provider1 = services.BuildServiceProvider();
        var options1 = provider1.GetRequiredService<IOptionsMonitor<SyncOptions>>().Get("dynamic");

        options1.ApiMode.Should().Be(ApiMode.Public);
        options1.ApiKey.Should().BeNull();

        services.Configure<SyncOptions>("dynamic", options =>
        {
            options.EnvironmentId = environmentId;
            options.ApiMode = ApiMode.Preview;
            options.ApiKey = "new-preview-key";
        });

        var provider2 = services.BuildServiceProvider();
        var options2 = provider2.GetRequiredService<IOptionsMonitor<SyncOptions>>().Get("dynamic");

        options2.ApiMode.Should().Be(ApiMode.Preview);
        options2.ApiKey.Should().Be("new-preview-key");
    }
}
