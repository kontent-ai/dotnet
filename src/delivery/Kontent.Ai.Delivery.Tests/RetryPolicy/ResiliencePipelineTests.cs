using AwesomeAssertions;
using Kontent.Ai.Common.Http;
using Polly.Timeout;

namespace Kontent.Ai.Delivery.Tests.RetryPolicy;

public class ResiliencePipelineTests
{
    // The default pipeline is retry-outside, timeout-inside, so a hung attempt reaches the retry as a
    // TimeoutRejectedException. Sync pins the composed behaviour; here the shared predicate itself.
    [Fact]
    public void IsTransientException_TimeoutRejectedException_ReturnsTrue()
    {
        HttpRetryPredicates.IsTransientException(new TimeoutRejectedException(), CancellationToken.None)
            .Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_CallerCancellation_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        HttpRetryPredicates.IsTransientException(new OperationCanceledException(), cts.Token)
            .Should().BeFalse();
    }
}
