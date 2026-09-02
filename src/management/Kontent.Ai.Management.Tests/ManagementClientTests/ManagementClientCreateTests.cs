using AwesomeAssertions;
using Kontent.Ai.Management.Configuration;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Management.Tests.ManagementClientTests;

public class ManagementClientCreateTests
{
    private static Action<ManagementOptions> ValidOptions => o =>
    {
        o.EnvironmentId = Guid.NewGuid().ToString();
        o.ApiKey = "valid-key";
    };

    [Fact]
    public void Create_Configured_ReturnsClient()
    {
        using var client = ManagementClient.Create(management => management.Options.Configure(ValidOptions));

        client.Should().NotBeNull().And.BeAssignableTo<IManagementClient>();
    }

    [Fact]
    public void Create_FromInstance_ReturnsClient()
    {
        var options = new ManagementOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiKey = "valid-key",
        };

        using var client = ManagementClient.Create(options);

        client.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ClientIsAsyncDisposable()
    {
        var client = ManagementClient.Create(management => management.Options.Configure(ValidOptions));

        await ((IAsyncDisposable)client).Invoking(c => c.DisposeAsync().AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public void ConfigureResilience_ReturnsTheSameBuilder()
    {
        IManagementClientBuilder? seen = null;
        IManagementClientBuilder? returned = null;

        using var client = ManagementClient.Create(management =>
        {
            seen = management;
            management.Options.Configure(ValidOptions);
            returned = management.ConfigureResilience(_ => { });
        });

        returned.Should().BeSameAs(seen);
    }

    [Fact]
    public void Builder_ExposesBothTransports()
    {
        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(ValidOptions);
            management.HttpClient.Name.Should().Be("Kontent.Ai.Management.HttpClient.Default");
            management.SubscriptionHttpClient.Name.Should().Be("Kontent.Ai.Management.SubscriptionHttpClient.Default");
        });
    }

    [Fact]
    public void Create_NullDelegate_Throws()
    {
        Action act = () => ManagementClient.Create((Action<IManagementClientBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullInstance_Throws()
    {
        Action act = () => ManagementClient.Create((ManagementOptions)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConfigureResilience_Null_Throws()
    {
        Action act = () => ManagementClient.Create(management =>
        {
            management.Options.Configure(ValidOptions);
            management.ConfigureResilience(null!);
        });

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("no-guid", "key")]
    [InlineData("00000000-0000-0000-0000-000000000000", "key")]
    [InlineData("4ee3d5cc-2e5b-4c81-9f4c-6a8f7b5d3c1e", null)]
    public void Create_InvalidOptions_ThrowsOptionsValidationException(string? envId, string? apiKey)
    {
        Action act = () => ManagementClient.Create(management => management.Options.Configure(o =>
        {
            o.EnvironmentId = envId!;
            o.ApiKey = apiKey!;
        }));

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Constructor_InvalidOptions_ThrowsOptionsValidationException()
    {
        Action act = () => new ManagementClient(new ManagementOptions { EnvironmentId = "no-guid", ApiKey = "key" });

        act.Should().Throw<OptionsValidationException>();
    }
}
