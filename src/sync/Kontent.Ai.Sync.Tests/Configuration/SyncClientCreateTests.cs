using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"items":[],"types":[],"languages":[],"taxonomies":[]}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            response.Headers.TryAddWithoutValidation("X-Continuation", "next-token");
            return response;
        });
        return captured;
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
