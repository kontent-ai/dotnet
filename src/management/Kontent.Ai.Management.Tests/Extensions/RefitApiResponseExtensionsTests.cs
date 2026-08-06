using AwesomeAssertions;
using Kontent.Ai.Management.Extensions;
using Refit;
using System.Net;

namespace Kontent.Ai.Management.Tests.Extensions;

public class RefitApiResponseExtensionsTests
{
    [Fact]
    public async Task ToManagementResultAsync_Canceled_Throws()
    {
        // Refit captures handler-chain exceptions into Error instead of throwing. Cancellation must not
        // be reported as a failed result, or Task.IsCanceled stays unset and cancellation handlers
        // upstream never fire.
        using var apiResponse = CreateTransportFailure(new OperationCanceledException("caller went away"));

        var act = async () => await apiResponse.ToManagementResultAsync();

        await act.Should().ThrowAsync<OperationCanceledException>().WithMessage("caller went away");
    }

    [Fact]
    public async Task ToManagementResultAsync_TimedOut_ReturnsFailureRatherThanCancellation()
    {
        // An expired HttpClient.Timeout arrives shaped like cancellation. On a write API, reporting it as
        // the caller's own cancellation invites the conclusion that nothing was sent - but the request went
        // out and the server may have applied it.
        using var apiResponse = CreateTransportFailure(
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));

        var result = await apiResponse.ToManagementResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error!.Exception!.InnerException.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task ToManagementResultAsync_TransportFailure_ReturnsFailure()
    {
        using var apiResponse = CreateTransportFailure(new HttpRequestException("No such host is known."));

        var result = await apiResponse.ToManagementResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Exception.Should().NotBeNull();
        result.Error.Exception.InnerException.Should().BeOfType<HttpRequestException>();
    }

    private static ApiResponse<string> CreateTransportFailure(Exception inner)
    {
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://manage.kontent.ai/test")
        };
        var settings = new RefitSettings();

        return new ApiResponse<string>(
            httpResponse,
            null,
            settings,
            new ApiRequestException("request failed", new HttpRequestMessage(), HttpMethod.Get, settings, inner));
    }
}
