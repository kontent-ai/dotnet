using System.Net;
using AwesomeAssertions;
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
    public async Task EnumerateDeltaAsync_YieldsEveryPage_AndStopsOnTheEmptyResponse()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: 3);
        var page2 = CreateSuccessDeltaResponse(nextToken: "token-3", itemCount: 2);
        var empty = CreateSuccessDeltaResponse(nextToken: "token-4", itemCount: 0);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(page2), Task.FromResult(empty));

        var pages = await client.EnumerateDeltaAsync("token-1").ToListAsync();

        pages.Should().HaveCount(2);
        pages.Should().OnlyContain(p => p.IsSuccess);
        pages[^1].SyncToken.Should().Be("token-3");

        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-1", Arg.Any<CancellationToken>());
        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-2", Arg.Any<CancellationToken>());
        _ = syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, "token-3", Arg.Any<CancellationToken>());
    }

    // The previous implementation stopped as soon as a page came back with fewer than 100 entries,
    // which would have ended the walk here after one page and silently skipped the rest.
    [Fact]
    public async Task EnumerateDeltaAsync_PartialPage_KeepsGoing()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var partial = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: 1);
        var more = CreateSuccessDeltaResponse(nextToken: "token-3", itemCount: 1);
        var empty = CreateSuccessDeltaResponse(nextToken: "token-4", itemCount: 0);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(partial), Task.FromResult(more), Task.FromResult(empty));

        var pages = await client.EnumerateDeltaAsync("token-1").ToListAsync();

        pages.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnumerateDeltaAsync_AlreadyUpToDate_YieldsNothing()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var empty = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: 0);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(empty));

        var pages = await client.EnumerateDeltaAsync("token-1").ToListAsync();

        pages.Should().BeEmpty();
    }

    [Fact]
    public async Task EnumerateDeltaAsync_WhenAPageFails_YieldsTheFailureAndStops()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: 3);
        var failure = await CreateFailedDeltaResponse(HttpStatusCode.Unauthorized, "Unauthorized");

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(failure));

        var pages = await client.EnumerateDeltaAsync("token-1").ToListAsync();

        pages.Should().HaveCount(2);
        pages[0].IsSuccess.Should().BeTrue();
        pages[1].IsSuccess.Should().BeFalse();
        pages[1].Error.Should().NotBeNull();
    }

    // Bounding the walk is the caller's job now; nothing beyond the requested page is fetched.
    [Fact]
    public async Task EnumerateDeltaAsync_IsLazy_SoTakeStopsTheRequests()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        // Every page reports changes, so nothing but Take bounds the walk. Built up front: creating a
        // substitute inside a Returns() argument re-enters NSubstitute's call context and deadlocks.
        var page1 = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: 1);
        var page2 = CreateSuccessDeltaResponse(nextToken: "token-3", itemCount: 1);
        var page3 = CreateSuccessDeltaResponse(nextToken: "token-4", itemCount: 1);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(page2), Task.FromResult(page3));

        var pages = await client.EnumerateDeltaAsync("token-1").Take(2).ToListAsync();

        pages.Should().HaveCount(2);
        _ = syncApi.Received(2).GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnumerateDeltaAsync_WithCancelledToken_ThrowsAndDoesNotCallApi()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await client.EnumerateDeltaAsync("token-1", cancellation.Token).ToListAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
        _ = syncApi.DidNotReceiveWithAnyArgs().GetDeltaAsync(default!, default!, default);
    }

    [Fact]
    public async Task EnumerateDeltaAsync_WhenContinuationHeaderMissing_ReusesPreviousToken()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestEnvironmentId);

        var page1 = CreateSuccessDeltaResponse(nextToken: null, itemCount: 1);
        var empty = CreateSuccessDeltaResponse(nextToken: "token-final", itemCount: 0);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page1), Task.FromResult(empty));

        var pages = await client.EnumerateDeltaAsync("token-initial").ToListAsync();

        pages.Should().HaveCount(1);
        _ = syncApi.Received(2).GetDeltaAsync(TestEnvironmentId, "token-initial", Arg.Any<CancellationToken>());
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
