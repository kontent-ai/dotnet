using System.Net;
using FluentAssertions;
using Kontent.Ai.Sync.Extensions;
using NSubstitute;
using Refit;
using Xunit;

namespace Kontent.Ai.Sync.Tests.Extensions;

public class RefitApiResponseExtensionsTests
{
    [Fact]
    public async Task ToSyncResultAsync_SuccessfulResponse_ReturnsSyncResult()
    {
        // Arrange
        var content = "test-content";
        var requestUrl = "https://test.com/api";
        var apiResponse = CreateSuccessResponse(content, requestUrl);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(content);
        result.RequestUrl.Should().Be(requestUrl);
        result.StatusCode.Should().Be(200);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ToSyncResultAsync_SuccessfulResponse_ExtractsSyncToken()
    {
        // Arrange
        var syncToken = "test-sync-token-12345";
        var apiResponse = CreateSuccessResponse("content", "https://test.com", syncToken);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.SyncToken.Should().Be(syncToken);
    }

    [Fact]
    public async Task ToSyncResultAsync_SuccessfulResponse_NoSyncToken_ReturnsNull()
    {
        // Arrange
        var apiResponse = CreateSuccessResponse("content", "https://test.com");

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.SyncToken.Should().BeNull();
    }

    [Fact]
    public async Task ToSyncResultAsync_FailedResponse_WithErrorContent_ParsesError()
    {
        // Arrange
        var errorJson = @"{
            ""message"": ""Test error message"",
            ""request_id"": ""req-123"",
            ""error_code"": 404,
            ""specific_code"": 1001
        }";
        var apiResponse = await CreateFailedResponse<string>(HttpStatusCode.NotFound, "https://test.com", errorJson);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be("Test error message");
        result.Error.RequestId.Should().Be("req-123");
        result.Error.ErrorCode.Should().Be(404);
        result.Error.SpecificCode.Should().Be(1001);
    }

    [Fact]
    public async Task ToSyncResultAsync_FailedResponse_WithInvalidJson_CreatesGenericError()
    {
        // Arrange
        var invalidJson = "{ invalid json }";
        var apiResponse = await CreateFailedResponse<string>(HttpStatusCode.BadRequest, "https://test.com", invalidJson);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be(invalidJson);
    }

    [Fact]
    public async Task ToSyncResultAsync_FailedResponse_NoErrorContent_CreatesGenericError()
    {
        // Arrange
        var reasonPhrase = "Not Found";
        var apiResponse = await CreateFailedResponse<string>(HttpStatusCode.NotFound, "https://test.com", null, reasonPhrase);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be(reasonPhrase);
        result.Error.ErrorCode.Should().Be(404);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    public async Task ToSyncResultAsync_FailedResponse_PreservesStatusCode(HttpStatusCode httpStatusCode, int expectedCode)
    {
        // Arrange
        var apiResponse = await CreateFailedResponse<string>(httpStatusCode, "https://test.com", null);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.StatusCode.Should().Be(expectedCode);
    }

    [Fact]
    public async Task ToSyncResultAsync_SuccessfulResponse_ComplexType_PreservesValue()
    {
        // Arrange
        var complexObject = new TestData { Id = 123, Name = "Test" };
        var apiResponse = CreateSuccessResponse(complexObject, "https://test.com");

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.Value.Should().BeEquivalentTo(complexObject);
    }

    [Fact]
    public async Task ToSyncResultAsync_NullContent_ReturnsFailure()
    {
        // Arrange
        var apiResponse = Substitute.For<IApiResponse<string>>();
        apiResponse.IsSuccessStatusCode.Returns(true);
        apiResponse.Content.Returns((string?)null);
        apiResponse.StatusCode.Returns(HttpStatusCode.OK);
        apiResponse.RequestMessage.Returns((HttpRequestMessage?)null);

        // Act
        var result = await apiResponse.ToSyncResultAsync();

        // Assert
        result.IsSuccess.Should().BeFalse("null content should be treated as failure");
    }

    // Helper methods
    private static IApiResponse<T> CreateSuccessResponse<T>(T content, string requestUrl, string? syncToken = null)
    {
        var apiResponse = Substitute.For<IApiResponse<T>>();
        apiResponse.IsSuccessStatusCode.Returns(true);
        apiResponse.Content.Returns(content);
        apiResponse.StatusCode.Returns(HttpStatusCode.OK);

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        apiResponse.RequestMessage.Returns(requestMessage);

        // Create a real HttpResponseMessage to get real headers
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        if (syncToken != null)
        {
            httpResponse.Headers.TryAddWithoutValidation("X-Continuation", syncToken);
        }
        apiResponse.Headers.Returns(httpResponse.Headers);

        return apiResponse;
    }

    private static async Task<IApiResponse<T>> CreateFailedResponse<T>(HttpStatusCode statusCode, string requestUrl, string? errorContent, string? reasonPhrase = null)
    {
        var apiResponse = Substitute.For<IApiResponse<T>>();
        apiResponse.IsSuccessStatusCode.Returns(false);
        apiResponse.StatusCode.Returns(statusCode);
        apiResponse.ReasonPhrase.Returns(reasonPhrase ?? statusCode.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        apiResponse.RequestMessage.Returns(requestMessage);

        if (errorContent != null)
        {
            // Create a real ApiException with the error content
            var error = await ApiException.Create(
                requestMessage,
                HttpMethod.Get,
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(errorContent),
                    ReasonPhrase = reasonPhrase
                },
                new RefitSettings());
            apiResponse.Error.Returns(error);
        }

        // Create a real HttpResponseMessage to get real headers (empty)
        var httpResponse = new HttpResponseMessage(statusCode);
        apiResponse.Headers.Returns(httpResponse.Headers);

        return apiResponse;
    }

    public class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
