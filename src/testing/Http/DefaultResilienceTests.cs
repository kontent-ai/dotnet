// Shared test source, compiled into each test assembly - see src/testing/README.md.

using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Kontent.Ai.Common.Http;
using Polly;
using Polly.Timeout;

namespace Kontent.Ai.Testing.Http;

/// <summary>
/// Pins the composed read pipeline in every product that compiles <see cref="DefaultResilience"/>.
/// </summary>
public class DefaultResilienceTests
{
    [Fact]
    public async Task ConfigureReadPipeline_RetriesOnTransientStatusCode()
    {
        var pipeline = Build();

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
    public async Task ConfigureReadPipeline_DoesNotRetryOnNonRetryableStatusCode()
    {
        var pipeline = Build();

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
    public async Task ConfigureReadPipeline_RetriesAnAttemptTheTimeoutRejected()
    {
        var pipeline = Build();

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

    private static ResiliencePipeline<HttpResponseMessage> Build()
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        DefaultResilience.ConfigureReadPipeline(builder);
        return builder.Build();
    }

    private static HttpResponseMessage WithRetryAfter(HttpResponseMessage response, TimeSpan delta)
    {
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        return response;
    }
}
