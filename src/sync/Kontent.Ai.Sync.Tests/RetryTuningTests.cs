using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Sync.Tests;

public sealed class RetryTuningTests
{
    [Fact]
    public async Task TuneRetry_PreservesRetryAfterAndDefaultHttpClientCeiling()
    {
        using var http = new MockHttpMessageHandler();
        var attempts = 0;
        http.When("https://example.test/*").Respond(_ =>
        {
            attempts++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });
        var services = new ServiceCollection();
        ISyncClientBuilder builder = null!;
        services.AddSyncClient("tuned", sync =>
        {
            builder = sync;
            sync.Options.Configure(o => o.EnvironmentId = Guid.NewGuid().ToString());
            sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => http);
        });
        builder.TuneRetry(retry => retry.MaxRetryAttempts = 5).Should().BeSameAs(builder);
        builder.TuneRetry(retry =>
        {
            retry.MaxRetryAttempts.Should().Be(5);
            retry.OnRetry = args =>
            {
                args.RetryDelay.Should().Be(TimeSpan.Zero);
                return ValueTask.CompletedTask;
            };
        });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(builder.HttpClient.Name);

        using var response = await client.GetAsync("https://example.test/items");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        attempts.Should().Be(6);
        client.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void TuneRetry_StandaloneClientReceivesInitializedDefaults()
    {
        var invoked = false;
        using var client = SyncClient.Create(sync =>
        {
            sync.Options.Configure(o => o.EnvironmentId = Guid.NewGuid().ToString());
            sync.TuneRetry(retry =>
            {
                retry.MaxRetryAttempts.Should().Be(3);
                retry.Delay.Should().Be(TimeSpan.FromSeconds(1));
                retry.MaxRetryAttempts = 5;
                invoked = true;
            });
        });

        invoked.Should().BeTrue();
    }

    [Fact]
    public void TuneRetry_NullCallbackIsRejected()
    {
        ISyncClientBuilder builder = null!;
        new ServiceCollection().AddSyncClient(sync => builder = sync);

        var act = () => builder.TuneRetry(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
