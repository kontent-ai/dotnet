using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.SharedModels;
using Xunit;

namespace Kontent.Ai.Sync.Tests.Models;

public class SyncResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResult()
    {
        // Arrange
        var value = "test-value";
        var requestUrl = "https://test.com/api";
        var statusCode = 200;
        var syncToken = "test-sync-token";

        // Act
        var result = SyncResult.Success(value, requestUrl, statusCode, syncToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(value);
        result.RequestUrl.Should().Be(requestUrl);
        result.StatusCode.Should().Be(statusCode);
        result.SyncToken.Should().Be(syncToken);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Success_DefaultStatusCode_Is200()
    {
        // Act
        var result = SyncResult.Success("value", "https://test.com");

        // Assert
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public void Success_NullSyncToken_IsAccepted()
    {
        // Act
        var result = SyncResult.Success("value", "https://test.com", syncToken: null);

        // Assert
        result.SyncToken.Should().BeNull();
    }

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        // Arrange
        var requestUrl = "https://test.com/api";
        var statusCode = 404;
        var error = new Error
        {
            Message = "Not found",
            ErrorCode = 404,
            RequestId = "req-123"
        };

        // Act
        var result = SyncResult.Failure<string>(requestUrl, statusCode, error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
        result.RequestUrl.Should().Be(requestUrl);
        result.StatusCode.Should().Be(statusCode);
        result.SyncToken.Should().BeNull();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Failure_NullError_IsAccepted()
    {
        // Act
        var result = SyncResult.Failure<string>("https://test.com", 500, null);

        // Assert
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Success_WithComplexType_PreservesValue()
    {
        // Arrange
        var complexValue = new { Id = 1, Name = "Test" };

        // Act
        var result = SyncResult.Success(complexValue, "https://test.com");

        // Assert
        result.Value.Should().BeEquivalentTo(complexValue);
    }

    [Fact]
    public void Failure_WithComplexType_ReturnsDefaultValue()
    {
        // Arrange
        var error = new Error { Message = "Test error" };

        // Act
        var result = SyncResult.Failure<int>("https://test.com", 400, error);

        // Assert
        result.Value.Should().Be(0, "default(int) is 0");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void StatusCode_IsPreserved(int statusCode)
    {
        // Act - Success
        var successResult = SyncResult.Success("value", "url", statusCode);

        // Assert
        successResult.StatusCode.Should().Be(statusCode);

        // Act - Failure
        var failureResult = SyncResult.Failure<string>("url", statusCode, null);

        // Assert
        failureResult.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public void Error_ContainsAllProperties()
    {
        // Arrange
        var error = new Error
        {
            Message = "Test error message",
            ErrorCode = 400,
            SpecificCode = 1001,
            RequestId = "req-abc-123"
        };

        // Act
        var result = SyncResult.Failure<string>("https://test.com", 400, error);

        // Assert
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be("Test error message");
        result.Error.ErrorCode.Should().Be(400);
        result.Error.SpecificCode.Should().Be(1001);
        result.Error.RequestId.Should().Be("req-abc-123");
    }

    [Fact]
    public void Success_EmptyRequestUrl_IsAccepted()
    {
        // Act
        var result = SyncResult.Success("value", string.Empty);

        // Assert
        result.RequestUrl.Should().BeEmpty();
    }

    [Fact]
    public void Failure_EmptyRequestUrl_IsAccepted()
    {
        // Act
        var result = SyncResult.Failure<string>(string.Empty, 500, null);

        // Assert
        result.RequestUrl.Should().BeEmpty();
    }
}
