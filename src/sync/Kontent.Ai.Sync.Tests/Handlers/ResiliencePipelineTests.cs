using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Polly;
using Polly.Timeout;

namespace Kontent.Ai.Sync.Tests.Handlers;

/// <summary>
/// The composed default pipeline. The shared predicates and delay generator it is built from are
/// pinned by the shared test sources under <c>Testing/Http</c>.
/// </summary>
public class ResiliencePipelineTests
{
    [Fact]
    public async Task ConfigureDefaultResilience_RetriesOnTransientStatusCode()
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ServiceCollectionExtensions.ConfigureDefaultResilience(builder);
        var pipeline = builder.Build();

        var attempts = 0;
        var response = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 2
                ? ValueTask.FromResult(WithRetryAfter(new HttpResponseMessage(HttpStatusCode.TooManyRequests), TimeSpan.Zero))
                : ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ConfigureDefaultResilience_DoesNotRetryOnNonRetryableStatusCode()
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ServiceCollectionExtensions.ConfigureDefaultResilience(builder);
        var pipeline = builder.Build();

        var attempts = 0;
        var response = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ConfigureDefaultResilience_RetriesAnAttemptTheTimeoutRejected()
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ServiceCollectionExtensions.ConfigureDefaultResilience(builder);
        var pipeline = builder.Build();

        var attempts = 0;
        var response = await pipeline.ExecuteAsync<HttpResponseMessage>(_ =>
        {
            attempts++;
            // What the inner timeout strategy surfaces to the retry when an attempt hangs.
            return attempts < 2
                ? throw new TimeoutRejectedException()
                : ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        attempts.Should().Be(2);
    }

    private static HttpResponseMessage WithRetryAfter(HttpResponseMessage response, TimeSpan delta)
    {
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        return response;
    }
}
