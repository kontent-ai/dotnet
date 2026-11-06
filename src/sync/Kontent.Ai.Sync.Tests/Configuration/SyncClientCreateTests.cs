using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Sync.Tests.Configuration;

public sealed class SyncClientCreateTests : IDisposable
{
    private const string EnvironmentId = "550cec62-90a6-4ab3-b3e4-3d0bb4c04f5c";
    private const string ProductionSyncUrl = $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync";
    private const string PreviewSyncUrl = $"https://preview-deliver.kontent.ai/v2/{EnvironmentId}/sync";

    private readonly MockHttpMessageHandler _http = new();

    public void Dispose() => _http.Dispose();

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
    public async Task Create_ProductionApi_SendsToTheProductionEndpointWithoutAKey()
    {
        var captured = ExpectDelta(ProductionSyncUrl);

        using var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization.Should().BeNull();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_PreviewApi_SendsToThePreviewEndpointWithTheKey()
    {
        var captured = ExpectDelta(PreviewSyncUrl);

        using var client = CreateClient(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UsePreviewApi("preview.api.key");
        });

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization!.Parameter.Should().Be("preview.api.key");
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_SecureApi_SendsToTheProductionEndpointWithTheKey()
    {
        var captured = ExpectDelta(ProductionSyncUrl);

        using var client = CreateClient(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.UseProductionApi("secure.api.key");
        });

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization!.Parameter.Should().Be("secure.api.key");
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_FromOptionsInstance_CopiesTheValues()
    {
        var captured = ExpectDelta(PreviewSyncUrl);
        var options = new SyncOptions { EnvironmentId = EnvironmentId }.UsePreviewApi("preview.api.key");

        using var client = SyncClient.Create(options, sync => sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization!.Parameter.Should().Be("preview.api.key");
        _http.VerifyNoOutstandingExpectation();
    }

    // The copy contract: the instance is copied, not held, so a change to it after Create stays with the caller.
    [Fact]
    public async Task Create_FromOptionsInstance_MutatingItAfterwardsDoesNotReachTheClient()
    {
        var captured = ExpectDelta(ProductionSyncUrl);
        var options = new SyncOptions { EnvironmentId = EnvironmentId };
        using var client = SyncClient.Create(options, sync => sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

        options.UsePreviewApi("preview.api.key");

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.Authorization.Should().BeNull();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void Create_InvalidOptions_ThrowsOptionsValidationException()
    {
        var act = () => SyncClient.Create(sync => sync.Options.Configure(o => o.EnvironmentId = "not-a-guid"));

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task Create_WithLoggerFactory_LogsThroughIt()
    {
        ExpectDelta(ProductionSyncUrl);
        var entries = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(entries));

        using var client = CreateClient(
            o => o.EnvironmentId = EnvironmentId,
            sync => sync.Services.AddSingleton(loggerFactory));

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        entries.Categories.Should().Contain(category => category.StartsWith("Kontent.Ai.Sync", StringComparison.Ordinal));
    }

    // Create builds a container and only then finds the options invalid; the container must not leak.
    [Fact]
    public void Create_WhenConstructionFails_DisposesThePrivateContainer()
    {
        DisposableProbe? probe = null;

        var act = () => SyncClient.Create(sync =>
        {
            sync.Services.AddSingleton(_ => probe = new DisposableProbe());
            sync.Options.Configure<DisposableProbe>((o, _) => o.EnvironmentId = "not-a-guid");
        });

        act.Should().Throw<OptionsValidationException>();
        probe.Should().NotBeNull();
        probe.Disposed.Should().BeTrue();
    }

    // Each client owns its own transport, so disposing one must leave the other usable.
    [Fact]
    public async Task Create_MultipleCalls_ProduceIndependentClients()
    {
        using var http1 = new MockHttpMessageHandler();
        using var http2 = new MockHttpMessageHandler();
        http2.Expect(HttpMethod.Get, ProductionSyncUrl).Respond(_ => EmptyDelta());

        var client1 = SyncClient.Create(sync =>
        {
            sync.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => http1);
        });
        using var client2 = SyncClient.Create(sync =>
        {
            sync.Options.Configure(o => o.EnvironmentId = EnvironmentId);
            sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => http2);
        });

        await client1.DisposeAsync();

        (await client2.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        http2.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_SendsTheSdkTrackingHeader()
    {
        var captured = ExpectDelta(ProductionSyncUrl);

        using var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        captured.Request!.Headers.GetValues("X-KC-SDKID").Should().ContainSingle()
            .Which.Should().StartWith("nuget.org;Kontent.Ai.Sync;");
    }

    [Fact]
    public async Task Create_RetriesTransientFailures()
    {
        _http.Expect(HttpMethod.Get, ProductionSyncUrl).Respond(HttpStatusCode.ServiceUnavailable);
        _http.Expect(HttpMethod.Get, ProductionSyncUrl).Respond(_ => EmptyDelta());

        using var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_WithResilienceDisabled_DoesNotRetry()
    {
        _http.Expect(HttpMethod.Get, ProductionSyncUrl).Respond(HttpStatusCode.ServiceUnavailable);

        using var client = CreateClient(o =>
        {
            o.EnvironmentId = EnvironmentId;
            o.EnableResilience = false;
        });

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeFalse();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_CustomResilience_ReplacesTheDefault()
    {
        // One retry where the default allows three, so a third response is never requested.
        _http.Expect(HttpMethod.Get, ProductionSyncUrl).Respond(HttpStatusCode.ServiceUnavailable);
        _http.Expect(HttpMethod.Get, ProductionSyncUrl).Respond(HttpStatusCode.ServiceUnavailable);

        using var client = CreateClient(
            o => o.EnvironmentId = EnvironmentId,
            sync => sync.ConfigureResilience(builder => builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.IsSuccessStatusCode == false),
            })));

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeFalse();
        _http.VerifyNoOutstandingExpectation();
    }

    // DisposeAsync and Dispose are separate paths through the owned resources, so each is pinned.
    [Fact]
    public async Task DisposeAsync_StopsFurtherRequests()
    {
        _http.When(HttpMethod.Get, ProductionSyncUrl).Respond(_ => EmptyDelta());

        SyncClient client;
        await using (client = CreateClient(o => o.EnvironmentId = EnvironmentId))
        {
            (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        }

        await AssertRequestsFail(client);
    }

    [Fact]
    public async Task Dispose_StopsFurtherRequests()
    {
        _http.When(HttpMethod.Get, ProductionSyncUrl).Respond(_ => EmptyDelta());

        SyncClient client;
        using (client = CreateClient(o => o.EnvironmentId = EnvironmentId))
        {
            (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        }

        await AssertRequestsFail(client);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        client.Dispose();
        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var client = CreateClient(o => o.EnvironmentId = EnvironmentId);

        await client.DisposeAsync();
        var act = async () => await client.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    // The transport failure is reported as a result rather than thrown, as every non-cancellation
    // failure in this SDK is; what matters here is that the request no longer reaches the handler.
    private static async Task AssertRequestsFail(SyncClient client)
    {
        var afterDispose = await client.GetDeltaAsync("token");

        afterDispose.IsSuccess.Should().BeFalse();
        afterDispose.Error!.Exception.Should().BeOfType<Refit.ApiRequestException>()
            .Which.InnerException.Should().BeOfType<ObjectDisposedException>();
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

    private SyncClient CreateClient(Action<SyncOptions> configureOptions, Action<ISyncClientBuilder>? configure = null)
        => SyncClient.Create(sync =>
        {
            sync.Options.Configure(configureOptions);
            sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http);
            configure?.Invoke(sync);
        });

    private CapturedRequest ExpectDelta(string url)
    {
        var captured = new CapturedRequest();
        _http.Expect(HttpMethod.Get, url).Respond(request =>
        {
            captured.Request = request;
            return EmptyDelta();
        });
        return captured;
    }

    private static HttpResponseMessage EmptyDelta()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"items":[],"types":[],"languages":[],"taxonomies":[]}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        response.Headers.TryAddWithoutValidation("X-Continuation", "next-token");
        return response;
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

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Categories { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, Categories);

        public void Dispose() { }

        private sealed class CollectingLogger(string category, ConcurrentBag<string> categories) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => categories.Add(category);
        }
    }
}
