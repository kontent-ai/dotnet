using System.Net;
using AwesomeAssertions;
using Kontent.Ai.Sync.Extensions;
using NSubstitute;
using Refit;

namespace Kontent.Ai.Sync.Tests.Extensions;

public class RefitApiResponseExtensionsTests
{
    [Fact]
    public async Task ToSyncResultAsync_SuccessfulResponse_ReturnsValueTokenHeadersAndStatus()
    {
        var apiResponse = CreateSuccessResponse(
            content: "payload",
            requestUrl: "https://test.com/sync",
            syncToken: "token-1");

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("payload");
        result.RequestUrl.Should().Be("https://test.com/sync");
        result.SyncToken.Should().Be("token-1");
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.ResponseHeaders.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ToSyncResultAsync_ApiExceptionWithStructuredError_ParsesError()
    {
        const string json = """
            {
              "message": "The sync token is invalid.",
              "request_id": "req-123",
              "error_code": 400,
              "specific_code": 1010
            }
            """;

        var apiResponse = await CreateFailedResponse<string>(
            statusCode: HttpStatusCode.BadRequest,
            requestUrl: "https://test.com/sync",
            errorContent: json);

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Error.Should().NotBeNull();
        result.Error.Message.Should().Be("The sync token is invalid.");
        result.Error.RequestId.Should().Be("req-123");
        result.Error.ErrorCode.Should().Be(400);
        result.Error.SpecificCode.Should().Be(1010);
        result.Error.Exception.Should().BeOfType<ApiException>();
    }

    [Fact]
    public async Task ToSyncResultAsync_ApiExceptionWithNonJsonBody_UsesFallbackAndKeepsException()
    {
        const string rawBody = "<html><body>gateway down</body></html>";
        var apiResponse = await CreateFailedResponse<string>(
            statusCode: HttpStatusCode.BadGateway,
            requestUrl: "https://test.com/sync",
            errorContent: rawBody);

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Message.Should().Contain("Raw response:");
        result.Error.Message.Should().Contain("gateway down");
        result.Error.Exception.Should().BeOfType<AggregateException>();
        var aggregate = (AggregateException)result.Error.Exception;
        aggregate.InnerExceptions.Should().HaveCount(2);
        aggregate.InnerExceptions[0].Should().BeOfType<ApiException>();
    }

    [Fact]
    public async Task ToSyncResultAsync_WithoutApiException_UsesUnknownMessageFallback()
    {
        var apiResponse = CreateFailedResponseWithoutApiException<string>(
            statusCode: HttpStatusCode.ServiceUnavailable,
            requestUrl: "https://test.com/sync");

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Message.Should().Be("Unknown error");
        result.Error.Exception.Should().BeNull();
    }

    [Fact]
    public async Task ToSyncResultAsync_WithNullContentTreatsResponseAsFailure()
    {
        var apiResponse = Substitute.For<IApiResponse<string>>();
        apiResponse.IsSuccessStatusCode.Returns(true);
        apiResponse.Content.Returns((string?)null);
        apiResponse.StatusCode.Returns(HttpStatusCode.OK);
        apiResponse.RequestMessage.Returns((HttpRequestMessage?)null);

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task ToSyncResultAsync_SuccessfulResponse_DisposesTheResponse()
    {
        var apiResponse = CreateSuccessResponse("payload", "https://sync.kontent.ai/test", "token");

        var result = await apiResponse.ToSyncResultAsync();

        apiResponse.Received(1).Dispose();
        // Headers survive disposal, which is what lets the result keep carrying them.
        result.ResponseHeaders.Should().NotBeNull();
    }

    [Fact]
    public async Task ToSyncResultAsync_FailedResponse_DisposesTheResponse()
    {
        var apiResponse = await CreateFailedResponse<string>(HttpStatusCode.NotFound, "https://sync.kontent.ai/test", null);

        await apiResponse.ToSyncResultAsync();

        apiResponse.Received(1).Dispose();
    }

    private static IApiResponse<T> CreateSuccessResponse<T>(T content, string requestUrl, string? syncToken = null)
    {
        var apiResponse = Substitute.For<IApiResponse<T>>();
        apiResponse.IsSuccessStatusCode.Returns(true);
        apiResponse.Content.Returns(content);
        apiResponse.StatusCode.Returns(HttpStatusCode.OK);

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        apiResponse.RequestMessage.Returns(requestMessage);

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        if (syncToken is not null)
        {
            httpResponse.Headers.TryAddWithoutValidation("X-Continuation", syncToken);
        }

        apiResponse.Headers.Returns(httpResponse.Headers);
        return apiResponse;
    }

    private static IApiResponse<T> CreateFailedResponseWithoutApiException<T>(
        HttpStatusCode statusCode,
        string requestUrl)
    {
        var apiResponse = Substitute.For<IApiResponse<T>>();
        apiResponse.IsSuccessStatusCode.Returns(false);
        apiResponse.StatusCode.Returns(statusCode);
        apiResponse.Error.Returns((ApiException?)null);
        apiResponse.RequestMessage.Returns(new HttpRequestMessage(HttpMethod.Get, requestUrl));

        var httpResponse = new HttpResponseMessage(statusCode);
        apiResponse.Headers.Returns(httpResponse.Headers);

        return apiResponse;
    }

    private static async Task<IApiResponse<T>> CreateFailedResponse<T>(
        HttpStatusCode statusCode,
        string requestUrl,
        string? errorContent)
    {
        var apiResponse = Substitute.For<IApiResponse<T>>();
        apiResponse.IsSuccessStatusCode.Returns(false);
        apiResponse.StatusCode.Returns(statusCode);

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        apiResponse.RequestMessage.Returns(requestMessage);

        var httpResponse = new HttpResponseMessage(statusCode);
        if (errorContent is not null)
        {
            httpResponse.Content = new StringContent(errorContent);
        }

        var apiException = await ApiException.Create(
            requestMessage,
            HttpMethod.Get,
            httpResponse,
            new RefitSettings());

        apiResponse.Error.Returns(apiException);
        apiResponse.Headers.Returns(httpResponse.Headers);

        return apiResponse;
    }

    [Fact]
    public async Task ToSyncResultAsync_Canceled_Throws()
    {
        // Refit captures handler-chain exceptions into Error instead of throwing. Cancellation must not
        // be reported as a failed result, or Task.IsCanceled stays unset and cancellation handlers
        // upstream never fire.
        var apiResponse = CreateTransportFailure(new OperationCanceledException("caller went away"));

        var act = async () => await apiResponse.ToSyncResultAsync();

        await act.Should().ThrowAsync<OperationCanceledException>().WithMessage("caller went away");
    }

    [Fact]
    public async Task ToSyncResultAsync_TimedOut_ReturnsFailureRatherThanCancellation()
    {
        // An expired HttpClient.Timeout arrives shaped like cancellation, but it is an outcome of the call,
        // not the caller withdrawing it - the result pattern owns it.
        var apiResponse = CreateTransportFailure(
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error!.Exception.Should().BeOfType<ApiRequestException>();
    }

    [Fact]
    public async Task ToSyncResultAsync_TransportFailure_ReturnsFailureCarryingTheRealMessage()
    {
        var apiResponse = CreateTransportFailure(new HttpRequestException("No such host is known."));

        var result = await apiResponse.ToSyncResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(default(HttpStatusCode));
        result.Error!.Message.Should().Be("No such host is known.");
    }

    private static IApiResponse<string> CreateTransportFailure(Exception inner)
    {
        var apiResponse = Substitute.For<IApiResponse<string>>();
        apiResponse.IsSuccessStatusCode.Returns(false);
        apiResponse.StatusCode.Returns((HttpStatusCode?)null);
        apiResponse.Content.Returns((string?)null);
        apiResponse.Error.Returns(new ApiRequestException("request failed", new HttpRequestMessage(), HttpMethod.Get, new RefitSettings(), inner));
        return apiResponse;
    }
}
