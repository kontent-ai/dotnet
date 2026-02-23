using System.Net;
using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Models;
using Kontent.Ai.Sync.SharedModels;

namespace Kontent.Ai.Sync.Tests.Models;

public class SyncResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult_WithHeadersAndToken()
    {
        var headers = new HttpResponseMessage(HttpStatusCode.OK).Headers;
        headers.TryAddWithoutValidation("X-Continuation", "token-123");

        var result = SyncResult.Success(
            value: "test-value",
            requestUrl: "https://test.com/sync",
            statusCode: HttpStatusCode.OK,
            syncToken: "token-123",
            responseHeaders: headers);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test-value");
        result.Error.Should().BeNull();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.SyncToken.Should().Be("token-123");
        result.RequestUrl.Should().Be("https://test.com/sync");
        result.ResponseHeaders.Should().BeSameAs(headers);
    }

    [Fact]
    public void Success_DeltaResponseAtLimit_SetsHasMoreChangesTrue()
    {
        var delta = new SyncDeltaResponse
        {
            Items = Enumerable.Range(0, SyncConstants.MaxItemsPerEntityType)
                .Select(_ => new SyncItem { ChangeType = ChangeType.Changed, Data = new { value = 1 } })
                .ToList()
        };

        var result = SyncResult.Success<ISyncDeltaResponse>(delta, "https://test.com/sync");

        result.HasMoreChanges.Should().BeTrue();
    }

    [Fact]
    public void Success_DeltaResponseBelowLimit_SetsHasMoreChangesFalse()
    {
        var delta = new SyncDeltaResponse
        {
            Items = Enumerable.Range(0, SyncConstants.MaxItemsPerEntityType - 1)
                .Select(_ => new SyncItem { ChangeType = ChangeType.Changed, Data = new { value = 1 } })
                .ToList()
        };

        var result = SyncResult.Success<ISyncDeltaResponse>(delta, "https://test.com/sync");

        result.HasMoreChanges.Should().BeFalse();
    }

    [Fact]
    public void Failure_CreatesFailedResult_WithHeaders()
    {
        var headers = new HttpResponseMessage(HttpStatusCode.NotFound).Headers;
        headers.TryAddWithoutValidation("X-Request-ID", "req-1");

        var error = new Error
        {
            Message = "Not found",
            ErrorCode = 404,
            RequestId = "req-1"
        };

        var result = SyncResult.Failure<string>(
            requestUrl: "https://test.com/sync",
            statusCode: HttpStatusCode.NotFound,
            error: error,
            responseHeaders: headers);

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be(error);
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        result.SyncToken.Should().BeNull();
        result.ResponseHeaders.Should().BeSameAs(headers);
        result.HasMoreChanges.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void StatusCode_IsPreserved(HttpStatusCode statusCode)
    {
        var successResult = SyncResult.Success("value", "url", statusCode);
        successResult.StatusCode.Should().Be(statusCode);

        var failureResult = SyncResult.Failure<string>("url", statusCode, null);
        failureResult.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public void Error_ExposesUnderlyingException()
    {
        var exception = new InvalidOperationException("boom");
        var error = new Error
        {
            Message = "Operation failed",
            ErrorCode = 500,
            SpecificCode = 1001,
            RequestId = "req-abc-123",
            Exception = exception
        };

        var result = SyncResult.Failure<string>("https://test.com", HttpStatusCode.InternalServerError, error);

        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be("Operation failed");
        result.Error.ErrorCode.Should().Be(500);
        result.Error.SpecificCode.Should().Be(1001);
        result.Error.RequestId.Should().Be("req-abc-123");
        result.Error.Exception.Should().BeSameAs(exception);
    }
}
