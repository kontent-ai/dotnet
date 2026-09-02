using AwesomeAssertions;
using Kontent.Ai.Common;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Kontent.Ai.Management.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    private static readonly string ValidEnvironmentId = Guid.NewGuid().ToString();
    private const string ValidApiKey = "dummy-key";

    [Fact]
    public void AddManagementClient_DuplicateName_Throws()
    {
        var services = new ServiceCollection();
        services.AddManagementClient("production", management => management.Options.Configure(ConfigureValidOptions));

        Action act = () => services.AddManagementClient("production", management => management.Options.Configure(ConfigureValidOptions));

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
    public void AddManagementClient_InvalidName_Throws(string? name)
    {
        var services = new ServiceCollection();

        Action act = () => services.AddManagementClient(name!, management => management.Options.Configure(ConfigureValidOptions));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddManagementClient_Default_RegistersUnkeyedAndKeyedClient()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(ConfigureValidOptions));

        using var provider = services.BuildServiceProvider();

        var unkeyed = provider.GetRequiredService<IManagementClient>();
        var keyed = provider.GetRequiredKeyedService<IManagementClient>(NamedClients.Default);
        var fromFactory = provider.GetRequiredService<IManagementClientFactory>().Get();

        unkeyed.Should().BeSameAs(keyed);
        fromFactory.Should().BeSameAs(unkeyed);
    }

    [Fact]
    public void AddManagementClient_NamedClient_ResolvableByFactoryAndKey()
    {
        var services = new ServiceCollection();
        services.AddManagementClient("alt", management => management.Options.Configure(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
            options.ApiKey = "alt-key";
        }));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IManagementClientFactory>();

        var fromKey = provider.GetRequiredKeyedService<IManagementClient>("alt");
        var fromFactory = factory.Get("alt");

        fromKey.Should().BeSameAs(fromFactory);
    }

    [Fact]
    public void AddManagementClient_DefaultAndNamed_AreIndependent()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(ConfigureValidOptions));
        services.AddManagementClient("alt", management => management.Options.Configure(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
            options.ApiKey = "alt-key";
        }));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IManagementClientFactory>();

        factory.Get().Should().NotBeSameAs(factory.Get("alt"));
    }

    [Fact]
    public void Factory_Get_UnregisteredName_Throws()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(ConfigureValidOptions));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IManagementClientFactory>();

        Action act = () => factory.Get("missing");

        act.Should().Throw<InvalidOperationException>().WithMessage("*No management client registered with name 'missing'*");
    }

    [Fact]
    public void AddManagementClient_WithConfigurationSection_BindsOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Management:EnvironmentId"] = ValidEnvironmentId,
                ["Management:ApiKey"] = ValidApiKey,
                ["Management:EnableResilience"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Bind(configuration.GetSection("Management")));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<ManagementOptions>>().CurrentValue;

        options.EnvironmentId.Should().Be(ValidEnvironmentId);
        options.ApiKey.Should().Be(ValidApiKey);
        options.EnableResilience.Should().BeFalse();
    }

    [Fact]
    public void AddManagementClient_AppliesTheConfiguredTimeoutToTheHttpClient()
    {
        // HttpClient's 100-second default bounds the whole call, retries and backoff included, so it capped
        // exactly the long uploads this SDK's missing per-attempt timeout is meant to allow.
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(options =>
        {
            ConfigureValidOptions(options);
            options.Timeout = TimeSpan.FromMinutes(42);
        }));

        using var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient($"Kontent.Ai.Management.HttpClient.{NamedClients.Default}");

        httpClient.Timeout.Should().Be(TimeSpan.FromMinutes(42));
    }

    [Fact]
    public void ManagementOptions_DefaultTimeout_LeavesRoomForALargeUpload()
    {
        // Sized against the documented 2 GB asset limit — 30 minutes carries that over roughly 10 Mbps.
        new ManagementOptions().Timeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void AddManagementClient_RegistersManagementApiAndSubscriptionApi()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(options =>
        {
            ConfigureValidOptions(options);
            options.SubscriptionId = Guid.NewGuid().ToString();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetService<IManagementApi>().Should().NotBeNull();
        provider.GetService<ISubscriptionApi>().Should().NotBeNull();
        provider.GetRequiredKeyedService<IManagementApi>(NamedClients.Default).Should().NotBeNull();
        provider.GetRequiredKeyedService<ISubscriptionApi>(NamedClients.Default).Should().NotBeNull();
    }

    [Fact]
    public async Task AddManagementClient_WithoutSubscriptionId_ClientResolvesButSubscriptionEndpointsThrow()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(ConfigureValidOptions));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IManagementClient>();

        var act = () => client.ListSubscriptionProjectsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SubscriptionId is not configured*");
    }

    [Fact]
    public void AddManagementClient_WithoutSubscriptionId_ResolvingSubscriptionApiThrows()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(ConfigureValidOptions));

        using var provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<ISubscriptionApi>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*SubscriptionId is not configured*");
    }

    [Fact]
    public async Task AddManagementClient_FromConfigurationSection_AppliesHttpClientAndResilienceHooks()
    {
        var stub = new RecordingPrimaryHandler(_ => TooManyRequestsResponse());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Management:EnvironmentId"] = ValidEnvironmentId,
                ["Management:ApiKey"] = ValidApiKey,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddManagementClient(management =>
        {
            management.Options.Bind(configuration.GetSection("Management"));
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => stub);
            management.ConfigureResilience(_ => { });
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IManagementClient>();

        var result = await client.GetEnvironmentInformationAsync();

        // The empty pipeline replaced the default one: a 429 that the default retries surfaces after one attempt.
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        stub.Attempts.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddManagementClient_DefaultResilience_RetriesGet429AndReappliesAuthAndTrackingHandlers()
    {
        var stub = new RecordingPrimaryHandler(attempt => attempt == 1
            ? TooManyRequestsResponse()
            : EnvironmentInformationResponse());

        var services = new ServiceCollection();
        services.AddManagementClient(management =>
        {
            management.Options.Configure(ConfigureValidOptions);
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => stub);
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IManagementClient>();

        var result = await client.GetEnvironmentInformationAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.EnvironmentName.Should().Be("Production");
        stub.Attempts.Should().HaveCount(2);
        stub.Attempts.Should().AllSatisfy(attempt =>
        {
            attempt.Authorization.Should().Be($"Bearer {ValidApiKey}");
            attempt.SdkId.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public async Task AddManagementClient_ResilienceDisabled_DoesNotRetryAndSurfaces429()
    {
        var stub = new RecordingPrimaryHandler(_ => TooManyRequestsResponse());

        var services = new ServiceCollection();
        services.AddManagementClient(management =>
        {
            management.Options.Configure(options =>
            {
                ConfigureValidOptions(options);
                options.EnableResilience = false;
            });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => stub);
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IManagementClient>();

        var result = await client.GetEnvironmentInformationAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        stub.Attempts.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddManagementClient_SubscriptionIdOnly_ResolvesAndFailsOnlyOnEnvironmentCalls()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(options =>
        {
            options.SubscriptionId = Guid.NewGuid().ToString();
            options.ApiKey = ValidApiKey;
        }));
        var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IManagementClient>();

        client.Should().NotBeNull();

        // No environment-scoped HttpClient is registered for this client at all, so the failure is local
        // and names the option rather than reaching the API with an empty path segment.
        var act = async () => await client.GetEnvironmentInformationAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*EnvironmentId is not configured*");
    }

    [Fact]
    public void AddManagementClient_NeitherEnvironmentIdNorSubscriptionId_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(options => options.ApiKey = ValidApiKey));
        var provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IManagementClient>();

        act.Should().Throw<OptionsValidationException>();
    }

    private static void ConfigureValidOptions(ManagementOptions options)
    {
        options.EnvironmentId = ValidEnvironmentId;
        options.ApiKey = ValidApiKey;
    }

    private static HttpResponseMessage TooManyRequestsResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    private static HttpResponseMessage EnvironmentInformationResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"id":"{{ValidEnvironmentId}}","name":"Sample project","environment":"Production"}""",
            Encoding.UTF8,
            "application/json"),
    };

    /// <summary>
    /// Stands in for the network as the primary handler and records the auth/tracking headers of each attempt,
    /// proving that retries re-run the full handler chain rather than replaying a frozen request.
    /// </summary>
    private sealed class RecordingPrimaryHandler(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(string? Authorization, string? SdkId)> Attempts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts.Add((
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-KC-SDKID", out var values) ? string.Join(",", values) : null));

            var response = responder(Attempts.Count);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void AddManagementClient_KeepsThePooledPrimaryHandler()
    {
        // Refit's registration installs a plain HttpClientHandler as the primary. The SDK's connection
        // recycling runs after it and must be what sits at the bottom of the chain.
        var services = new ServiceCollection();
        services.AddManagementClient(management => management.Options.Configure(o =>
        {
            o.EnvironmentId = Guid.NewGuid().ToString();
            o.ApiKey = "dummy";
        }));
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("Kontent.Ai.Management.HttpClient.Default");
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        handler.Should().BeOfType<SocketsHttpHandler>()
            .Which.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(2));
    }

    // The consumer's chain runs after the SDK's own setup, so what it puts on the HTTP client wins. This is
    // what the old "configureHttpClient is applied last" guarantee became.
    [Fact]
    public void AddManagementClient_TheConsumersHttpClientChainRunsLast()
    {
        var marker = new HttpClientHandler();
        var services = new ServiceCollection();
        services.AddManagementClient(management =>
        {
            management.Options.Configure(ConfigureValidOptions);
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => marker);
        });
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("Kontent.Ai.Management.HttpClient.Default");
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        handler.Should().BeSameAs(marker);
    }
}
