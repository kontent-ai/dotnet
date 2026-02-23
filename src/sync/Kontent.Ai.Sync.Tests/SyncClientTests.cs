using System.Net;
using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Models;
using NSubstitute;
using Refit;

namespace Kontent.Ai.Sync.Tests;

public class SyncClientTests
{
    private const string TestEnvironmentId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task GetAllDeltaAsync_SuccessPagination_ReturnsAllPagesAndFinalToken()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: SyncConstants.MaxItemsPerEntityType);
        var page2 = CreateSuccessDeltaResponse(nextToken: "token-3", itemCount: 1);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(page2));

        var result = await client.GetAllDeltaAsync("token-1");

        result.IsSuccess.Should().BeTrue();
        result.PagesFetched.Should().Be(2);
        result.Responses.Should().HaveCount(2);
        result.FinalSyncToken.Should().Be("token-3");
        result.WasLimitedByMaxPages.Should().BeFalse();

        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-1", Arg.Any<CancellationToken>());
        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDeltaAsync_MaxPagesCap_StopsAndMarksResultAsLimited()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: SyncConstants.MaxItemsPerEntityType);
        var page2 = CreateSuccessDeltaResponse(nextToken: "token-3", itemCount: SyncConstants.MaxItemsPerEntityType);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(page2));

        var result = await client.GetAllDeltaAsync("token-1", maxPages: 2);

        result.IsSuccess.Should().BeTrue();
        result.PagesFetched.Should().Be(2);
        result.WasLimitedByMaxPages.Should().BeTrue();
        result.FinalSyncToken.Should().Be("token-3");

        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-1", Arg.Any<CancellationToken>());
        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDeltaAsync_WhenSecondPageFails_ReturnsFailureWithPartialResponses()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: SyncConstants.MaxItemsPerEntityType);
        var page2 = await CreateFailedDeltaResponse(HttpStatusCode.Unauthorized, "Unauthorized");

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(page2));

        var result = await client.GetAllDeltaAsync("token-1");

        result.IsSuccess.Should().BeFalse();
        result.PagesFetched.Should().Be(1);
        result.Responses.Should().HaveCount(1);
        result.FinalSyncToken.Should().Be("token-2");
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllDeltaAsync_WithCancelledToken_ThrowsAndDoesNotCallApi()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await client.GetAllDeltaAsync("token-1", cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _ = syncApi.DidNotReceiveWithAnyArgs().GetDeltaAsync(default!, default!, default);
    }

    [Fact]
    public async Task InitializeSyncAsync_Success_ReturnsSyncToken()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        httpResponse.Headers.TryAddWithoutValidation("X-Continuation", "init-token");

        var apiResponse = Substitute.For<IApiResponse<SyncInitResponse>>();
        apiResponse.IsSuccessStatusCode.Returns(true);
        apiResponse.StatusCode.Returns(HttpStatusCode.OK);
        apiResponse.Content.Returns(new SyncInitResponse());
        apiResponse.Headers.Returns(httpResponse.Headers);
        apiResponse.RequestMessage.Returns(new HttpRequestMessage(HttpMethod.Post, "https://deliver.kontent.ai/v2/sync/init"));

        syncApi.InitializeSyncAsync(TestEnvironmentId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(apiResponse));

        var result = await client.InitializeSyncAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncToken.Should().Be("init-token");
    }

    [Fact]
    public async Task InitializeSyncAsync_Failure_ReturnsError()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://deliver.kontent.ai/v2/sync/init");
        var httpResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"Unauthorized\"}")
        };

        var apiException = await ApiException.Create(request, HttpMethod.Post, httpResponse, new RefitSettings());

        var apiResponse = Substitute.For<IApiResponse<SyncInitResponse>>();
        apiResponse.IsSuccessStatusCode.Returns(false);
        apiResponse.StatusCode.Returns(HttpStatusCode.Unauthorized);
        apiResponse.Error.Returns(apiException);
        apiResponse.Headers.Returns(httpResponse.Headers);
        apiResponse.RequestMessage.Returns(request);

        syncApi.InitializeSyncAsync(TestEnvironmentId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(apiResponse));

        var result = await client.InitializeSyncAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDeltaAsync_Success_ReturnsResponseWithToken()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        httpResponse.Headers.TryAddWithoutValidation("X-Continuation", "next-token");

        var apiResponse = Substitute.For<IApiResponse<SyncDeltaResponse>>();
        apiResponse.IsSuccessStatusCode.Returns(true);
        apiResponse.StatusCode.Returns(HttpStatusCode.OK);
        apiResponse.Content.Returns(new SyncDeltaResponse
        {
            Items = [new SyncItem { ChangeType = ChangeType.Changed, Data = new { } }]
        });
        apiResponse.Headers.Returns(httpResponse.Headers);
        apiResponse.RequestMessage.Returns(new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync"));

        syncApi.GetDeltaAsync(TestEnvironmentId, "my-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(apiResponse));

        var result = await client.GetDeltaAsync("my-token");

        result.IsSuccess.Should().BeTrue();
        result.SyncToken.Should().Be("next-token");
        result.Value.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDeltaAsync_NullSyncToken_ThrowsArgumentException()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        Func<Task> act = async () => await client.GetDeltaAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetDeltaAsync_WhitespaceSyncToken_ThrowsArgumentException()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        Func<Task> act = async () => await client.GetDeltaAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAllDeltaAsync_WhenContinuationHeaderMissing_ReusesPreviousToken()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: null, itemCount: SyncConstants.MaxItemsPerEntityType);
        var page2 = CreateSuccessDeltaResponse(nextToken: "token-final", itemCount: 1);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(page2));

        var result = await client.GetAllDeltaAsync("token-initial");

        result.IsSuccess.Should().BeTrue();
        result.FinalSyncToken.Should().Be("token-final");

        _ = syncApi.Received(2).GetDeltaAsync(TestEnvironmentId, "token-initial", Arg.Any<CancellationToken>());
    }

    private static IApiResponse<SyncDeltaResponse> CreateSuccessDeltaResponse(string? nextToken, int itemCount)
    {
        var response = Substitute.For<IApiResponse<SyncDeltaResponse>>();
        response.IsSuccessStatusCode.Returns(true);
        response.StatusCode.Returns(HttpStatusCode.OK);
        response.RequestMessage.Returns(new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync"));

        response.Content.Returns(new SyncDeltaResponse
        {
            Items = Enumerable.Range(0, itemCount)
                .Select(i => new SyncItem { ChangeType = ChangeType.Changed, Data = new { index = i } })
                .ToList()
        });

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        if (nextToken is not null)
        {
            httpResponse.Headers.TryAddWithoutValidation("X-Continuation", nextToken);
        }

        response.Headers.Returns(httpResponse.Headers);
        return response;
    }

    private static async Task<IApiResponse<SyncDeltaResponse>> CreateFailedDeltaResponse(HttpStatusCode statusCode, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync");
        var httpResponse = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent($"{{\"message\":\"{message}\"}}")
        };

        var apiException = await ApiException.Create(request, HttpMethod.Get, httpResponse, new RefitSettings());

        var response = Substitute.For<IApiResponse<SyncDeltaResponse>>();
        response.IsSuccessStatusCode.Returns(false);
        response.StatusCode.Returns(statusCode);
        response.RequestMessage.Returns(request);
        response.Error.Returns(apiException);
        response.Headers.Returns(httpResponse.Headers);

        return response;
    }
}
