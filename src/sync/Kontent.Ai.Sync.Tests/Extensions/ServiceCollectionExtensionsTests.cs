using AwesomeAssertions;
using Polly;
using Polly.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Sync.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    private static readonly string EnvironmentId = Guid.NewGuid().ToString();

    [Fact]
    public void AddSyncClient_DuplicateName_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSyncClient("production", sync => sync.Options.Configure(options => options.EnvironmentId = Guid.NewGuid().ToString()));

        Action act = () => services.AddSyncClient("production", sync => sync.Options.Configure(options => options.EnvironmentId = Guid.NewGuid().ToString()));

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

        Action act = () => services.AddSyncClient(name!, sync => sync.Options.Configure(options => options.EnvironmentId = Guid.NewGuid().ToString()));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddSyncClient_DefaultAndNamedClientAccess_IsConsistent()
    {
        var services = new ServiceCollection();

        services.AddSyncClient(sync => sync.Options.Configure(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
        }));

        services.AddSyncClient("preview", sync => sync.Options.Configure(options =>
        {
            options.EnvironmentId = Guid.NewGuid().ToString();
            options.ApiMode = ApiMode.Preview;
            options.ApiKey = "preview-key";
        }));

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

    // The instance overload copies values onto the options the container materializes, rather than
    // registering the caller's object.
    [Fact]
    public void AddSyncClient_WithOptionsInstance_CopiesTheValues()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Secure,
            ApiKey = "secure-key",
        };

        var services = new ServiceCollection();
        services.AddSyncClient(options);
        using var provider = services.BuildServiceProvider();

        var registered = provider.GetRequiredService<IOptionsMonitor<SyncOptions>>().Get("Default");

        registered.EnvironmentId.Should().Be(EnvironmentId);
        registered.ApiMode.Should().Be(ApiMode.Secure);
        registered.ApiKey.Should().Be("secure-key");
    }

    [Fact]
    public void AddSyncClient_WithOptionsExtensions_ConfiguresTheValues()
    {
        var services = new ServiceCollection();
        services.AddSyncClient(sync => sync.Options.Configure(options =>
        {
            options.EnvironmentId = EnvironmentId;
            options.UsePreviewApi("preview-key");
        }));

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetRequiredService<IOptionsMonitor<SyncOptions>>().Get("Default");

        registered.EnvironmentId.Should().Be(EnvironmentId);
        registered.ApiMode.Should().Be(ApiMode.Preview);
        registered.ApiKey.Should().Be("preview-key");
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
        services.AddSyncClient(sync => sync.Options.Bind(configuration.GetSection("Sync")));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<SyncOptions>>().CurrentValue;

        options.EnvironmentId.Should().Be(environmentId);
        options.ApiMode.Should().Be(ApiMode.Preview);
        options.ApiKey.Should().Be("preview-key");
        options.EnableResilience.Should().BeFalse();
    }

    // The gap this closes: before, binding from IConfiguration and customizing the pipeline were
    // mutually exclusive. Working around it by binding inside an Action<SyncOptions> silently lost
    // reload, because that path registers no change token.
    [Fact]
    public void AddSyncClient_FromConfiguration_SupportsResilienceCustomizationAndStillReloads()
    {
        var source = new Dictionary<string, string?>
        {
            ["SyncOptions:EnvironmentId"] = EnvironmentId,
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(source).Build();
        var resilienceConfigured = false;

        var services = new ServiceCollection();
        services.AddSyncClient(sync =>
        {
            sync.Options.Bind(configuration.GetSection(SyncOptions.DefaultConfigurationSectionName));
            sync.ConfigureResilience(_ => resilienceConfigured = true);
        });

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SyncOptions>>();

        monitor.Get("Default").EnvironmentId.Should().Be(EnvironmentId);

        // Force the HTTP client to be built so the resilience callback runs.
        _ = provider.GetRequiredService<IHttpClientFactory>();
        _ = provider.GetRequiredService<ISyncClient>();
        resilienceConfigured.Should().BeTrue();

        // The binding is change-token backed, so a reload of the source is picked up. Set through the
        // root rather than the seed dictionary, which the in-memory provider copies at build time.
        var replacement = Guid.NewGuid().ToString();
        configuration["SyncOptions:EnvironmentId"] = replacement;
        configuration.Reload();

        monitor.Get("Default").EnvironmentId.Should().Be(replacement);
    }

    [Fact]
    public void AddSyncClient_RuntimeConfigurationChanges_AreReflectedInOptions()
    {
        var environmentId = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddSyncClient("dynamic", sync => sync.Options.Configure(options =>
        {
            options.EnvironmentId = environmentId;
            options.ApiMode = ApiMode.Public;
        }));

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

    // The HttpClient ceiling is the only thing bounding a request when the default resilience pipeline -
    // which carries the per-attempt timeout - is not the one installed.

    [Fact]
    public void AddSyncClient_DefaultResilience_LiftsTheHttpClientCeiling()
    {
        var timeout = ResolveHttpClientTimeout(services =>
            services.AddSyncClient("production", sync => sync.Options.Configure(options => options.EnvironmentId = EnvironmentId)));

        timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void AddSyncClient_ResilienceDisabled_StillBoundsTheRequest()
    {
        var timeout = ResolveHttpClientTimeout(services =>
            services.AddSyncClient("production", sync => sync.Options.Configure(options =>
            {
                options.EnvironmentId = EnvironmentId;
                options.EnableResilience = false;
            })));

        timeout.Should().NotBe(Timeout.InfiniteTimeSpan);
        timeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void AddSyncClient_CustomResilience_StillBoundsTheRequest()
    {
        var timeout = ResolveHttpClientTimeout(services =>
            services.AddSyncClient("production", sync =>
            {
                sync.Options.Configure(options => options.EnvironmentId = EnvironmentId);
                sync.ConfigureResilience(builder => builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>()));
            }));

        timeout.Should().NotBe(Timeout.InfiniteTimeSpan);
        timeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void AddSyncClient_ExplicitTimeout_OutranksTheDefaultPipelineLift()
    {
        var timeout = ResolveHttpClientTimeout(services =>
            services.AddSyncClient("production", sync => sync.Options.Configure(options =>
            {
                options.EnvironmentId = EnvironmentId;
                options.Timeout = TimeSpan.FromMinutes(5);
            })));

        timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AddSyncClient_ExplicitTimeout_AppliesWithACustomPipelineToo()
    {
        var timeout = ResolveHttpClientTimeout(services =>
            services.AddSyncClient("production", sync =>
            {
                sync.Options.Configure(options =>
                {
                    options.EnvironmentId = EnvironmentId;
                    options.Timeout = TimeSpan.FromMinutes(5);
                });
                sync.ConfigureResilience(builder => builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>()));
            }));

        timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AddSyncClient_InfiniteTimeout_RemovesTheCeilingWithResilienceDisabled()
    {
        var timeout = ResolveHttpClientTimeout(services =>
            services.AddSyncClient("production", sync => sync.Options.Configure(options =>
            {
                options.EnvironmentId = EnvironmentId;
                options.EnableResilience = false;
                options.Timeout = Timeout.InfiniteTimeSpan;
            })));

        timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    private static TimeSpan ResolveHttpClientTimeout(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        using var provider = services.BuildServiceProvider();
        using var httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("Kontent.Ai.Sync.HttpClient.production");

        return httpClient.Timeout;
    }

    [Fact]
    public void AddSyncClient_KeepsThePooledPrimaryHandler()
    {
        // Refit's registration installs a plain HttpClientHandler as the primary. The SDK's connection
        // recycling runs after it and must be what sits at the bottom of the chain.
        var services = new ServiceCollection();
        services.AddSyncClient(sync => sync.Options.Configure(o => o.EnvironmentId = Guid.NewGuid().ToString()));
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("Kontent.Ai.Sync.HttpClient.Default");
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
    public void AddSyncClient_TheConsumersHttpClientChainRunsLast()
    {
        var marker = new HttpClientHandler();
        var services = new ServiceCollection();
        services.AddSyncClient(sync =>
        {
            sync.Options.Configure(o => o.EnvironmentId = Guid.NewGuid().ToString());
            sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => marker);
        });
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("Kontent.Ai.Sync.HttpClient.Default");
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        handler.Should().BeSameAs(marker);
    }

    // The instance overload copies when the options are first built, not at registration.
    [Fact]
    public void AddSyncClient_WithOptionsInstance_CopiesWhenTheOptionsAreFirstBuilt()
    {
        var options = new SyncOptions { EnvironmentId = EnvironmentId };
        var replacement = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSyncClient(options);

        options.EnvironmentId = replacement;                      // before the first read: included
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SyncOptions>>();
        monitor.Get("Default").EnvironmentId.Should().Be(replacement);

        options.EnvironmentId = Guid.NewGuid().ToString();        // after it: not
        monitor.Get("Default").EnvironmentId.Should().Be(replacement);
    }

    [Fact]
    public async Task AddSyncClient_BindConfiguration_BindsTheSectionTheClientSendsTo()
    {
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Get, $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync").Respond(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[],"types":[],"languages":[],"taxonomies":[]}""", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("X-Continuation", "next-token");
            return response;
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sync:EnvironmentId"] = EnvironmentId })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSyncClient(sync =>
        {
            sync.Options.BindConfiguration("Sync");
            sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mockHttp);
        });
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISyncClient>().GetDeltaAsync("token");

        result.IsSuccess.Should().BeTrue();
        mockHttp.VerifyNoOutstandingExpectation();
    }
}
