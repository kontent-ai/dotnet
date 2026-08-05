using System.Net;
using AwesomeAssertions;
using Kontent.Ai.Sync.Abstractions;
using Polly;
using Polly.Retry;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Sync.Tests;

/// <summary>
/// Covers the container-free path: a client that builds and owns its own <see cref="HttpClient"/>.
/// The DI path is covered by <see cref="SyncTransportTests"/>; these assert the standalone client
/// assembles the same handler chain and genuinely owns what it built.
/// </summary>
public sealed class StandaloneClientTests : IDisposable
{
    private const string EnvironmentId = "00000000-0000-0000-0000-000000000001";
    private const string SyncUrl = $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync";

    private readonly MockHttpMessageHandler _http = new();

    public void Dispose() => _http.Dispose();

    private static SyncOptions Options(Action<SyncOptions>? configure = null)
    {
        var options = new SyncOptions { EnvironmentId = EnvironmentId };
        configure?.Invoke(options);
        return options;
    }

    private static HttpResponseMessage EmptyDelta()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"items":[],"types":[],"languages":[],"taxonomies":[]}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        response.Headers.TryAddWithoutValidation("X-Continuation", "next-token");
        return response;
    }

    [Fact]
    public async Task StandaloneClient_UsesTheSameHandlerChainAsTheContainer()
    {
        HttpRequestMessage? captured = null;
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(request =>
        {
            captured = request;
            return EmptyDelta();
        });

        using var client = new SyncClient(
            Options(o =>
            {
                o.ApiMode = ApiMode.Secure;
                o.ApiKey = "secure-key";
            }),
            primaryHandler: _http);

        var result = await client.GetDeltaAsync("token");

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured.Headers.Authorization!.Parameter.Should().Be("secure-key");
        captured.Headers.GetValues("X-KC-SDKID").Should().ContainSingle();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task StandaloneClient_RetriesTransientFailures()
    {
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(HttpStatusCode.ServiceUnavailable);
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(_ => EmptyDelta());

        using var client = new SyncClient(Options(), primaryHandler: _http);

        var result = await client.GetDeltaAsync("token");

        result.IsSuccess.Should().BeTrue();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task StandaloneClient_WithResilienceDisabled_DoesNotRetry()
    {
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(HttpStatusCode.ServiceUnavailable);

        using var client = new SyncClient(Options(o => o.EnableResilience = false), primaryHandler: _http);

        var result = await client.GetDeltaAsync("token");

        result.IsSuccess.Should().BeFalse();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task StandaloneClient_CustomResilience_ReplacesTheDefault()
    {
        // One retry where the default allows three, so the third response is never requested.
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(HttpStatusCode.ServiceUnavailable);
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(HttpStatusCode.ServiceUnavailable);

        using var client = new SyncClient(
            Options(),
            configureResilience: builder => builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.IsSuccessStatusCode == false),
            }),
            primaryHandler: _http);

        var result = await client.GetDeltaAsync("token");

        result.IsSuccess.Should().BeFalse();
        _http.VerifyNoOutstandingExpectation();
    }

    // The point of the standalone path owning its HttpClient: disposing the client releases it.
    // DisposeAsync delegates to Dispose, so this covers both.
    [Fact]
    public async Task DisposingTheClient_ReleasesItsHttpClient()
    {
        _http.When(HttpMethod.Get, SyncUrl).Respond(_ => EmptyDelta());

        ISyncClient client;
        await using (client = new SyncClient(Options(), primaryHandler: _http))
        {
            (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        }

        // The transport failure is reported as a result rather than thrown, as every non-cancellation
        // failure in this SDK is; what matters here is that the request no longer reaches the handler.
        var afterDispose = await client.GetDeltaAsync("token");

        afterDispose.IsSuccess.Should().BeFalse();
        afterDispose.Error.Should().NotBeNull();
        afterDispose.Error.Exception.Should().BeOfType<Refit.ApiRequestException>()
            .Which.InnerException.Should().BeOfType<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var client = new SyncClient(Options(), primaryHandler: _http);

        client.Dispose();
        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var client = new SyncClient(Options(), primaryHandler: _http);

        await client.DisposeAsync();
        var act = async () => await client.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PreviewMode_TargetsThePreviewEndpoint()
    {
        _http.Expect(HttpMethod.Get, $"https://preview-deliver.kontent.ai/v2/{EnvironmentId}/sync")
            .Respond(_ => EmptyDelta());

        using var client = new SyncClient(
            Options(o =>
            {
                o.ApiMode = ApiMode.Preview;
                o.ApiKey = "preview-key";
            }),
            primaryHandler: _http);

        (await client.GetDeltaAsync("token")).IsSuccess.Should().BeTrue();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void InvalidOptions_AreRejectedAtConstruction()
    {
        var act = () => new SyncClient(new SyncOptions { EnvironmentId = "not-a-guid" });

        act.Should().Throw<System.ComponentModel.DataAnnotations.ValidationException>();
    }
}
