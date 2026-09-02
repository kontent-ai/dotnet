using System.Net;
using System.Text;
using AwesomeAssertions;
using Kontent.Ai.Management.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Management.Tests.ManagementClientTests;

public sealed class ManagementClientCreateTests : IDisposable
{
    private readonly MockHttpMessageHandler _http = new();

    public void Dispose() => _http.Dispose();

    private static Action<ManagementOptions> ValidOptions => o =>
    {
        o.EnvironmentId = Guid.NewGuid().ToString();
        o.ApiKey = "valid-key";
    };

    [Fact]
    public async Task Create_Configured_SendsToTheEnvironmentWithTheKey()
    {
        var environmentId = Guid.NewGuid().ToString();
        var captured = ExpectEnvironmentInformation(environmentId);

        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o =>
            {
                o.EnvironmentId = environmentId;
                o.ApiKey = "valid-key";
            });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http);
        });

        (await client.GetEnvironmentInformationAsync()).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization!.Parameter.Should().Be("valid-key");
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_FromInstance_CopiesTheValues()
    {
        var environmentId = Guid.NewGuid().ToString();
        var captured = ExpectEnvironmentInformation(environmentId);
        var options = new ManagementOptions { EnvironmentId = environmentId, ApiKey = "valid-key" };

        using var client = ManagementClient.Create(options, management => management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

        (await client.GetEnvironmentInformationAsync()).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization!.Parameter.Should().Be("valid-key");
        _http.VerifyNoOutstandingExpectation();
    }

    // The copy contract: the instance is copied, not held, so a change to it after Create stays with the caller.
    [Fact]
    public async Task Create_FromInstance_MutatingItAfterwardsDoesNotReachTheClient()
    {
        var environmentId = Guid.NewGuid().ToString();
        var captured = ExpectEnvironmentInformation(environmentId);
        var options = new ManagementOptions { EnvironmentId = environmentId, ApiKey = "valid-key" };
        using var client = ManagementClient.Create(options, management => management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

        options.ApiKey = "rotated-key";

        (await client.GetEnvironmentInformationAsync()).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization!.Parameter.Should().Be("valid-key");
        _http.VerifyNoOutstandingExpectation();
    }

    // The point of Create handing the client its container: disposing the client tears the transports down.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisposingTheClient_FailsEveryFurtherRequest(bool enableResilience)
    {
        var environmentId = Guid.NewGuid().ToString();
        _http.When(HttpMethod.Get, $"https://manage.kontent.ai/v2/projects/{environmentId}").Respond(_ => EnvironmentInformationResponse(environmentId));
        ManagementClient client;
        await using (client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o =>
            {
                o.EnvironmentId = environmentId;
                o.ApiKey = "valid-key";
                o.EnableResilience = enableResilience;
            });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http);
        }))
        {
            (await client.GetEnvironmentInformationAsync()).IsSuccess.Should().BeTrue();
        }

        var afterDispose = await client.GetEnvironmentInformationAsync();

        afterDispose.IsSuccess.Should().BeFalse();
        HasObjectDisposed(afterDispose.Error?.Exception).Should().BeTrue(afterDispose.Error?.Exception?.ToString());
    }

    // Create builds a container and only then finds the options invalid; the container must not leak.
    [Fact]
    public void Create_WhenConstructionFails_DisposesThePrivateContainer()
    {
        DisposableProbe? probe = null;

        Action act = () => ManagementClient.Create(management =>
        {
            management.Services.AddSingleton(_ => probe = new DisposableProbe());
            management.Options.Configure<DisposableProbe>((o, _) =>
            {
                o.EnvironmentId = "no-guid";
                o.ApiKey = "key";
            });
        });

        act.Should().Throw<OptionsValidationException>();
        probe.Should().NotBeNull();
        probe.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task SubscriptionHttpClient_IsTheSubscriptionTransport()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        var environmentMarker = new MockHttpMessageHandler();   // nothing arranged: a request here fails the call
        _http.Expect(HttpMethod.Get, $"https://manage.kontent.ai/v2/subscriptions/{subscriptionId}/users")
            .Respond("application/json", """{"users":[],"pagination":{}}""");

        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o =>
            {
                o.EnvironmentId = Guid.NewGuid().ToString();
                o.SubscriptionId = subscriptionId;
                o.ApiKey = "valid-key";
            });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => environmentMarker);
            management.SubscriptionHttpClient.ConfigurePrimaryHttpMessageHandler(() => _http);
        });

        var result = await client.ListSubscriptionUsersAsync();

        result.IsSuccess.Should().BeTrue();
        _http.VerifyNoOutstandingExpectation();
    }

    // The default pipeline retries a 429 three times; an empty one surfaces it after one attempt - on the
    // subscription transport too.
    [Fact]
    public async Task ConfigureResilience_AppliesToTheSubscriptionTransport()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        var attempts = 0;
        _http.When(HttpMethod.Get, $"https://manage.kontent.ai/v2/subscriptions/{subscriptionId}/users").Respond(_ =>
        {
            attempts++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });

        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o =>
            {
                o.SubscriptionId = subscriptionId;
                o.ApiKey = "valid-key";
            });
            management.SubscriptionHttpClient.ConfigurePrimaryHttpMessageHandler(() => _http);
            management.ConfigureResilience(_ => { });
        });

        var result = await client.ListSubscriptionUsersAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        attempts.Should().Be(1);
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

    private CapturedRequest ExpectEnvironmentInformation(string environmentId)
    {
        var captured = new CapturedRequest();
        _http.Expect(HttpMethod.Get, $"https://manage.kontent.ai/v2/projects/{environmentId}").Respond(request =>
        {
            captured.Request = request;
            return EnvironmentInformationResponse(environmentId);
        });
        return captured;
    }

    private static HttpResponseMessage EnvironmentInformationResponse(string environmentId) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"id":"{{environmentId}}","name":"Sample project","environment":"Production"}""",
            Encoding.UTF8,
            "application/json"),
    };

    private static bool HasObjectDisposed(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ObjectDisposedException)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CapturedRequest
    {
        public HttpRequestMessage? Request { get; set; }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
