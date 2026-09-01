// Shared test source, compiled into each test assembly - see src/testing/README.md.

using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Kontent.Ai.Common.Http;
using Polly.Timeout;

namespace Kontent.Ai.Testing.Http;

/// <summary>
/// Pins <see cref="HttpRetryPredicates"/> identically in every product that compiles it, so a change
/// to what "transient" means cannot pass one product's tests while breaking another's.
/// </summary>
public class HttpRetryPredicatesTests
{
    public static TheoryData<HttpStatusCode, bool> RetryableStatusCodes => new()
    {
        { HttpStatusCode.RequestTimeout, true },
        { HttpStatusCode.TooManyRequests, true },
        { HttpStatusCode.InternalServerError, true },
        { HttpStatusCode.BadGateway, true },
        { HttpStatusCode.ServiceUnavailable, true },
        { HttpStatusCode.GatewayTimeout, true },
        { HttpStatusCode.OK, false },
        { HttpStatusCode.BadRequest, false },
        { HttpStatusCode.Unauthorized, false },
        { HttpStatusCode.Forbidden, false },
        { HttpStatusCode.NotFound, false },
        { HttpStatusCode.Conflict, false },
        { HttpStatusCode.UnprocessableEntity, false }
    };

    [Theory]
    [MemberData(nameof(RetryableStatusCodes))]
    public void IsRetryableStatusCode_MatchesExpected(HttpStatusCode code, bool expected)
    {
        HttpRetryPredicates.IsRetryableStatusCode(code).Should().Be(expected);
    }

    [Fact]
    public void IsRetryableStatusCode_Null_ReturnsFalse()
    {
        HttpRetryPredicates.IsRetryableStatusCode(null).Should().BeFalse();
    }

    [Fact]
    public void IsTransientException_Null_ReturnsFalse()
    {
        HttpRetryPredicates.IsTransientException(null, CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void IsTransientException_HttpRequestException_ReturnsTrue()
    {
        HttpRetryPredicates.IsTransientException(new HttpRequestException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_SocketException_WrappedInHttpRequestException_ReturnsTrue()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException("transient", inner);

        HttpRetryPredicates.IsTransientException(ex, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_TimeoutException_ReturnsTrue()
    {
        HttpRetryPredicates.IsTransientException(new TimeoutException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_TaskCanceledException_WithoutUserCancellation_ReturnsTrue()
    {
        HttpRetryPredicates.IsTransientException(new TaskCanceledException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_TaskCanceledException_WithTimeoutInner_ReturnsTrue()
    {
        var ex = new TaskCanceledException("http timeout", new TimeoutException());

        HttpRetryPredicates.IsTransientException(ex, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_OperationCanceled_UserCancelled_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        HttpRetryPredicates.IsTransientException(new OperationCanceledException(cts.Token), cts.Token).Should().BeFalse();
    }

    [Fact]
    public void IsTransientException_InvalidOperationException_ReturnsFalse()
    {
        HttpRetryPredicates.IsTransientException(new InvalidOperationException(), CancellationToken.None).Should().BeFalse();
    }

    // The pipeline's own per-attempt timeout surfaces to the retry as this exception. Pinned even in a
    // product whose pipeline adds no such timeout: the predicate is one file compiled three times.
    [Fact]
    public void IsTransientException_TimeoutRejectedException_ReturnsTrue()
    {
        HttpRetryPredicates.IsTransientException(new TimeoutRejectedException(), CancellationToken.None).Should().BeTrue();
    }
}
