using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Kontent.Ai.Management.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Kontent.Ai.Management.Tests.Handlers;

public sealed class RetryTuningTests
{
    [Theory]
    [InlineData("GET", HttpStatusCode.ServiceUnavailable, 6)]
    [InlineData("POST", HttpStatusCode.ServiceUnavailable, 1)]
    [InlineData("PATCH", HttpStatusCode.ServiceUnavailable, 1)]
    [InlineData("POST", HttpStatusCode.TooManyRequests, 6)]
    [InlineData("PATCH", HttpStatusCode.TooManyRequests, 6)]
    public async Task TuneRetry_PreservesWriteSafetyAndRetryAfter(string method, HttpStatusCode status, int expectedAttempts)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        var services = new ServiceCollection();
        IManagementClientBuilder builder = null!;
        services.AddManagementClient("tuned", management =>
        {
            builder = management;
            management.Options.Configure(o => { o.EnvironmentId = Guid.NewGuid().ToString(); o.ApiKey = "key"; });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        builder.TuneRetry(retry => retry.MaxRetryAttempts = 5).Should().BeSameAs(builder);
        builder.TuneRetry(retry =>
        {
            retry.MaxRetryAttempts.Should().Be(5);
            retry.Delay.Should().Be(TimeSpan.FromSeconds(1));
            retry.BackoffType.Should().Be(DelayBackoffType.Exponential);
            retry.UseJitter.Should().BeTrue();
            retry.OnRetry = args =>
            {
                args.RetryDelay.Should().Be(TimeSpan.Zero);
                return ValueTask.CompletedTask;
            };
        });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(builder.HttpClient.Name);

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Parse(method), "https://example.test/"));

        response.StatusCode.Should().Be(status);
        handler.Attempts.Should().Be(expectedAttempts);
    }

    [Theory]
    [InlineData("GET", 6)]
    [InlineData("POST", 1)]
    [InlineData("PATCH", 1)]
    public async Task TuneRetry_TransportFailurePreservesWriteSafety(string method, int expectedAttempts)
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Connection lost"));
        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o => { o.EnvironmentId = Guid.NewGuid().ToString(); o.ApiKey = "key"; });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
            management.HttpClient.AddHttpMessageHandler(() => new MethodHandler(HttpMethod.Parse(method)));
            management.TuneRetry(retry => { retry.MaxRetryAttempts = 5; retry.Delay = TimeSpan.Zero; });
        });

        var result = await client.GetEnvironmentInformationAsync();

        result.IsSuccess.Should().BeFalse();
        handler.Attempts.Should().Be(expectedAttempts);
    }

    [Fact]
    public async Task TuneRetry_UsesFreshDefaultsOnBothStandaloneTransports()
    {
        var options = new List<HttpRetryStrategyOptions>();
        var environment = new StubHandler(RateLimited);
        var subscription = new StubHandler(RateLimited);
        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o =>
            {
                o.EnvironmentId = Guid.NewGuid().ToString();
                o.SubscriptionId = Guid.NewGuid().ToString();
                o.ApiKey = "key";
            });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => environment);
            management.SubscriptionHttpClient.ConfigurePrimaryHttpMessageHandler(() => subscription);
            management.TuneRetry(retry =>
            {
                options.Add(retry);
                retry.MaxRetryAttempts += 2;
            });
        });

        (await client.GetEnvironmentInformationAsync()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.ListSubscriptionUsersAsync()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        environment.Attempts.Should().Be(6);
        subscription.Attempts.Should().Be(6);
        options.Should().HaveCount(2);
        options[0].Should().NotBeSameAs(options[1]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task TuneRetry_IsSkippedWhenDisabledOrReplaced(bool enabled, bool replacementFirst)
    {
        var handler = new StubHandler(RateLimited);
        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o =>
            {
                o.EnvironmentId = Guid.NewGuid().ToString();
                o.ApiKey = "key";
                o.EnableResilience = enabled;
            });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
            if (replacementFirst)
            {
                management.ConfigureResilience(_ => { });
            }
            management.TuneRetry(_ => throw new InvalidOperationException("Tuning must not run"));
            if (enabled && !replacementFirst)
            {
                management.ConfigureResilience(_ => { });
            }
        });

        (await client.GetEnvironmentInformationAsync()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        handler.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task TuneRetry_ExplicitPredicateReplacementIsHonored()
    {
        var handler = new StubHandler(RateLimited);
        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o => { o.EnvironmentId = Guid.NewGuid().ToString(); o.ApiKey = "key"; });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
            management.TuneRetry(retry => retry.ShouldHandle = _ => ValueTask.FromResult(false));
        });

        (await client.GetEnvironmentInformationAsync()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        handler.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task TuneRetry_CallerCancellationStopsRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        using var client = ManagementClient.Create(management =>
        {
            management.Options.Configure(o => { o.EnvironmentId = Guid.NewGuid().ToString(); o.ApiKey = "key"; });
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
            management.TuneRetry(retry => retry.MaxRetryAttempts = 5);
        });

        var act = () => client.GetEnvironmentInformationAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Attempts.Should().Be(1);
    }

    [Fact]
    public void TuneRetry_NullCallbackIsRejected()
    {
        IManagementClientBuilder builder = null!;
        new ServiceCollection().AddManagementClient(management => builder = management);

        var act = () => builder.TuneRetry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static Task<HttpResponseMessage> RateLimited(CancellationToken _)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return Task.FromResult(response);
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            var response = await respond(cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class MethodHandler(HttpMethod method) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method = method;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
