// Shared test source, compiled into each test assembly - see src/testing/README.md.

using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Kontent.Ai.Common.Http;
using Polly;
using Polly.Retry;

namespace Kontent.Ai.Testing.Http;

/// <summary>
/// Pins <see cref="HttpRetryDelay"/> in every product that compiles it.
/// </summary>
public class HttpRetryDelayTests
{
    [Fact]
    public async Task FromRetryAfterHeader_Returns429RetryAfterDelta()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var delay = await Invoke(response);

        delay.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task FromRetryAfterHeader_429WithoutHeader_ReturnsNull()
    {
        var delay = await Invoke(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        delay.Should().BeNull();
    }

    [Fact]
    public async Task FromRetryAfterHeader_Non429_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var delay = await Invoke(response);

        delay.Should().BeNull();
    }

    private static async Task<TimeSpan?> Invoke(HttpResponseMessage response)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            var args = new RetryDelayGeneratorArguments<HttpResponseMessage>(context, Outcome.FromResult(response), attemptNumber: 0);
            return await HttpRetryDelay.FromRetryAfterHeader(args);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
