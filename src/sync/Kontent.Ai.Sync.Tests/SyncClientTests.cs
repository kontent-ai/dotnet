using System.Net;
using AwesomeAssertions;
using Kontent.Ai.Common;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Models;
using NSubstitute;
using Refit;

namespace Kontent.Ai.Sync.Tests;

public class SyncClientTests
{
    private const string TestEnvironmentId = "00000000-0000-0000-0000-000000000001";

    private static IOptionsAccessor<SyncOptions> TestOptions()
    {
        var accessor = Substitute.For<IOptionsAccessor<SyncOptions>>();
        accessor.Current.Returns(new SyncOptions { EnvironmentId = TestEnvironmentId });
        return accessor;
    }

    [Fact]
    public async Task EnumerateDeltaAsync_YieldsEveryPage_AndStopsOnTheEmptyResponse()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestOptions());

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

    // A short page is not the end of the feed - only an empty response is.
    [Fact]
    public async Task EnumerateDeltaAsync_PartialPage_KeepsGoing()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestOptions());

        var partial = CreateSuccessDeltaResponse(nextToken: "token-2", itemCount: 1);
        var more = CreateSuccessDeltaResponse(nextToken: "token-3", itemCount: 1);
        var empty = CreateSuccessDeltaResponse(nextToken: "token-4", itemCount: 0);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(partial), Task.FromResult(more), Task.FromResult(empty));

        var pages = await client.EnumerateDeltaAsync("token-1").ToListAsync();

        pages.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnumerateDeltaAsync_RepeatedToken_StopsInsteadOfLooping()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestOptions());

        // The same token back with changes still in the page would otherwise re-request it forever.
        var stuck = CreateSuccessDeltaResponse(nextToken: "token-1", itemCount: 1);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(stuck));

        var pages = await client.EnumerateDeltaAsync("token-1").ToListAsync();

        pages.Should().ContainSingle();
        await syncApi.Received(1).GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnumerateDeltaAsync_AlreadyUpToDate_YieldsNothing()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestOptions());

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
        var client = new SyncClient(syncApi, TestOptions());

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
        var client = new SyncClient(syncApi, TestOptions());

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
        var client = new SyncClient(syncApi, TestOptions());

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await client.EnumerateDeltaAsync("token-1", cancellation.Token).ToListAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
        _ = syncApi.DidNotReceiveWithAnyArgs().GetDeltaAsync(default!, default!, default);
    }

    // The API issues a token with every successful response; without one there is no way to continue,
    // so the result is refused rather than handed back in a state the caller cannot act on.
    [Fact]
    public async Task GetDeltaAsync_ResponseWithoutContinuationToken_Throws()
    {
        var syncApi = Substitute.For<ISyncApi>();
        var client = new SyncClient(syncApi, TestOptions());

        var page = CreateSuccessDeltaResponse(nextToken: null, itemCount: 1);

        syncApi.GetDeltaAsync(TestEnvironmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page));

        Func<Task> act = async () => await client.GetDeltaAsync("token-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*X-Continuation*");
    }

    private static SyncItemSystem SystemFor(string codename) => new()
    {
        Id = Guid.NewGuid(),
        Collection = "default",
        Name = codename,
        Codename = codename,
        Language = "en-US",
        Type = "article",
        LastModified = DateTime.UnixEpoch,
    };

    private static IApiResponse<SyncDeltaResponse> CreateSuccessDeltaResponse(string? nextToken, int itemCount)
    {
        var response = Substitute.For<IApiResponse<SyncDeltaResponse>>();
        response.IsSuccessStatusCode.Returns(true);
        response.StatusCode.Returns(HttpStatusCode.OK);
        response.RequestMessage.Returns(new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync"));

        response.Content.Returns(new SyncDeltaResponse
        {
            Items = Enumerable.Range(0, itemCount)
                .Select(i => new SyncChange<SyncItemData>
                {
                    ChangeType = ChangeType.Changed,
                    Timestamp = DateTime.UnixEpoch.AddMinutes(i),
                    Data = new SyncItemData { System = SystemFor($"item_{i}") },
                })
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
