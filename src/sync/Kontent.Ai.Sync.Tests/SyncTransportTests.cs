using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Sync.Tests;

/// <summary>
/// Drives a client registered exactly as a consumer registers it, mocking at the transport rather than
/// stubbing <c>ISyncApi</c>. These are the only tests that execute the Refit route templates, the
/// handler chain and the serializer settings as they are actually wired - stubbing the API interface
/// starts below all three.
/// </summary>
public sealed class SyncTransportTests : IDisposable
{
    private const string EnvironmentId = "00000000-0000-0000-0000-000000000001";
    private const string SyncUrl = $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync";
    private const string InitUrl = $"{SyncUrl}/init";

    private readonly MockHttpMessageHandler _http = new();
    private ServiceProvider? _provider;

    private ISyncClient CreateClient(Action<SyncOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddSyncClient(
            options =>
            {
                options.EnvironmentId = EnvironmentId;
                configureOptions?.Invoke(options);
            },
            http => http.ConfigurePrimaryHttpMessageHandler(() => _http));

        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<ISyncClient>();
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _http.Dispose();
    }

    [Fact]
    public async Task InitializeSyncAsync_PostsToInitRoute_AndReadsContinuationHeader()
    {
        _http.Expect(HttpMethod.Post, InitUrl)
            .Respond(_ => WithContinuation(EmptyJsonObject(), "init-token"));

        var result = await CreateClient().InitializeSyncAsync();

        result.IsSuccess.Should().BeTrue();
        result.SyncToken.Should().Be("init-token");
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetDeltaAsync_GetsFromSyncRoute_SendingTokenAsContinuationHeader()
    {
        _http.Expect(HttpMethod.Get, SyncUrl)
            .WithHeaders("X-Continuation", "my-token")
            .Respond(_ => EmptyDelta());

        var result = await CreateClient().GetDeltaAsync("my-token");

        result.IsSuccess.Should().BeTrue();
        result.SyncToken.Should().Be("next-token");
        _http.VerifyNoOutstandingExpectation();
    }

    // Pins the wire contract against the configured serializer: snake_case property names and the
    // change_type enum token, neither of which is exercised when ISyncApi is stubbed.
    [Fact]
    public async Task GetDeltaAsync_DeserializesTheWireFormat()
    {
        const string body = """
            {
              "items": [
                { "change_type": "changed", "data": { "system": { "codename": "article" } } },
                { "change_type": "deleted", "data": null }
              ],
              "types": [],
              "languages": [],
              "taxonomies": []
            }
            """;

        _http.Expect(HttpMethod.Get, SyncUrl)
            .Respond(_ => WithContinuation(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                },
                "next-token"));

        var result = await CreateClient().GetDeltaAsync("token");

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].ChangeType.Should().Be(ChangeType.Changed);
        result.Value.Items[1].ChangeType.Should().Be(ChangeType.Deleted);
        result.Value.Types.Should().BeEmpty();
    }

    [Fact]
    public async Task Requests_CarryTheSdkTrackingHeader()
    {
        HttpRequestMessage? captured = null;
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(request =>
        {
            captured = request;
            return EmptyDelta();
        });

        await CreateClient().GetDeltaAsync("token");

        captured.Should().NotBeNull();
        captured.Headers.GetValues("X-KC-SDKID").Should().ContainSingle()
            .Which.Should().StartWith("nuget.org;Kontent.Ai.Sync;");
    }

    [Fact]
    public async Task SecureMode_SendsBearerAuthorization()
    {
        HttpRequestMessage? captured = null;
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(request =>
        {
            captured = request;
            return EmptyDelta();
        });

        await CreateClient(options =>
        {
            options.ApiMode = ApiMode.Secure;
            options.ApiKey = "secure-key";
        }).GetDeltaAsync("token");

        captured.Should().NotBeNull();
        captured.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("secure-key");
    }

    [Fact]
    public async Task PublicMode_SendsNoAuthorization()
    {
        HttpRequestMessage? captured = null;
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(request =>
        {
            captured = request;
            return EmptyDelta();
        });

        await CreateClient().GetDeltaAsync("token");

        captured.Should().NotBeNull();
        captured.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task PreviewMode_TargetsThePreviewEndpoint()
    {
        _http.Expect(HttpMethod.Get, $"https://preview-deliver.kontent.ai/v2/{EnvironmentId}/sync")
            .Respond(_ => EmptyDelta());

        var result = await CreateClient(options =>
        {
            options.ApiMode = ApiMode.Preview;
            options.ApiKey = "preview-key";
        }).GetDeltaAsync("token");

        result.IsSuccess.Should().BeTrue();
        _http.VerifyNoOutstandingExpectation();
    }

    // Proves the resilience handler is actually attached to the registered client, not merely that
    // ConfigureDefaultResilience builds a pipeline in isolation.
    [Fact]
    public async Task TransientFailure_IsRetriedByTheRegisteredPipeline()
    {
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(HttpStatusCode.ServiceUnavailable);
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(_ => EmptyDelta());

        var result = await CreateClient().GetDeltaAsync("token");

        result.IsSuccess.Should().BeTrue();
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task DisabledResilience_DoesNotRetry()
    {
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(HttpStatusCode.ServiceUnavailable);

        var result = await CreateClient(options => options.EnableResilience = false).GetDeltaAsync("token");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ErrorEnvelope_IsMappedToAFailureResult()
    {
        _http.Expect(HttpMethod.Get, SyncUrl).Respond(
            HttpStatusCode.Unauthorized,
            "application/json",
            """{"message":"The API key is invalid.","request_id":"abc123","error_code":2}""");

        var result = await CreateClient().GetDeltaAsync("token");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        result.Error.Should().NotBeNull();
        result.Error.Message.Should().Be("The API key is invalid.");
        result.Error.RequestId.Should().Be("abc123");
        result.Error.ErrorCode.Should().Be(2);
    }

    // Every successful Sync API response carries a continuation token, so fixtures must too.
    private static HttpResponseMessage EmptyDelta() => WithContinuation(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"items":[],"types":[],"languages":[],"taxonomies":[]}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        },
        "next-token");

    private static HttpResponseMessage WithContinuation(HttpResponseMessage response, string token)
    {
        response.Headers.TryAddWithoutValidation("X-Continuation", token);
        return response;
    }

    private static HttpResponseMessage EmptyJsonObject() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
    };
}
