using System.Globalization;
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
    // change_type enum token, neither of which is exercised when ISyncApi is stubbed. The payloads
    // are the documented examples verbatim, so a drift between this SDK and the published schema
    // shows up here rather than against a live environment.
    [Fact]
    public async Task GetDeltaAsync_DeserializesTheWireFormat()
    {
        const string body = """
            {
              "items": [
                {
                  "change_type": "changed",
                  "timestamp": "2025-06-20T13:03:06.1310204Z",
                  "data": {
                    "system": {
                      "id": "335d17ac-b6ba-4c6a-ae31-23c1193215cb",
                      "collection": "default",
                      "name": "My article",
                      "codename": "my_article",
                      "language": "en-US",
                      "type": "article",
                      "sitemap_locations": [],
                      "last_modified": "2019-03-27T13:21:11.38Z",
                      "workflow": "default",
                      "workflow_step": "published"
                    }
                  }
                }
              ],
              "types": [
                {
                  "change_type": "changed",
                  "timestamp": "2025-06-20T13:03:06.1310204Z",
                  "data": {
                    "system": {
                      "id": "b2c14f2c-6467-460b-a70b-bca17972a33a",
                      "name": "Article",
                      "codename": "article",
                      "last_modified": "2019-10-20T12:03:17.4685693Z"
                    }
                  }
                }
              ],
              "languages": [
                {
                  "change_type": "changed",
                  "timestamp": "2025-06-20T13:03:06.1310204Z",
                  "data": {
                    "system": {
                      "id": "00000000-0000-0000-0000-000000000000",
                      "name": "Default language",
                      "codename": "default"
                    }
                  }
                }
              ],
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

        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.ChangeType.Should().Be(ChangeType.Changed);
        item.Timestamp.Should().Be(DateTime.Parse("2025-06-20T13:03:06.1310204Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        item.Timestamp.Kind.Should().Be(DateTimeKind.Utc, "the API always sends UTC");
        item.Data.System.Codename.Should().Be("my_article");
        item.Data.System.Collection.Should().Be("default");
        item.Data.System.Language.Should().Be("en-US");
        item.Data.System.Workflow.Should().Be("default");
        item.Data.System.WorkflowStep.Should().Be("published");

        // Deprecated and scheduled for removal, so deliberately not modelled - its presence in the
        // payload must not upset deserialization.
        result.Value.Types.Should().ContainSingle().Which.Data.System.Codename.Should().Be("article");

        // The narrowest payload: no last_modified, and nothing the API guarantees.
        var language = result.Value.Languages.Should().ContainSingle().Subject;
        language.Data.System.Codename.Should().Be("default");

        result.Value.Taxonomies.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDeltaAsync_DeletedEntry_CarriesTheDeletedEntitysMetadata()
    {
        const string body = """
            {
              "items": [
                {
                  "change_type": "deleted",
                  "timestamp": "2025-06-20T13:03:06.1310204Z",
                  "data": {
                    "system": {
                      "id": "335d17ac-b6ba-4c6a-ae31-23c1193215cb",
                      "collection": "default",
                      "name": "My article",
                      "codename": "my_article",
                      "language": "en-US",
                      "type": "article",
                      "last_modified": "2019-03-27T13:21:11.38Z",
                      "workflow": "default",
                      "workflow_step": "published"
                    }
                  }
                }
              ],
              "types": [],
              "languages": [],
              "taxonomies": []
            }
            """;

        var result = await RespondWithAsync(body);

        result.IsSuccess.Should().BeTrue();
        var deleted = result.Value.Items.Should().ContainSingle().Subject;
        deleted.ChangeType.Should().Be(ChangeType.Deleted);
        deleted.Data.System.Codename.Should().Be("my_article");
    }

    [Theory]
    [InlineData("""{ "change_type": "deleted", "timestamp": "2025-06-20T13:03:06.1310204Z", "data": null }""")]
    [InlineData("""{ "change_type": "deleted", "timestamp": "2025-06-20T13:03:06.1310204Z" }""")]
    public async Task GetDeltaAsync_EntryWithoutUsableData_FailsInsteadOfYieldingNull(string entry)
    {
        var body = $$"""
            {
              "items": [ {{entry}} ],
              "types": [],
              "languages": [],
              "taxonomies": []
            }
            """;

        var result = await RespondWithAsync(body);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Message.Should().Contain("could not be read");
        result.Error.Exception.Should().NotBeNull();
        result.Error.Exception.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
        result.Error.ErrorCode.Should().BeNull("the HTTP status is not an API error code");
    }

    [Theory]
    [InlineData("""{ "id": "00000000-0000-0000-0000-000000000000", "name": "Default language" }""")]
    [InlineData("""{ "id": "00000000-0000-0000-0000-000000000000", "name": "Default language", "codename": null }""")]
    public async Task GetDeltaAsync_LanguageWithoutItsIdentity_FailsInsteadOfYieldingNull(string system)
    {
        // Every language has an id, a name and a codename, whatever the reference's markers say.
        var body = $$"""
            {
              "items": [],
              "types": [],
              "languages": [ { "change_type": "changed", "timestamp": "2025-06-20T13:03:06.1310204Z", "data": { "system": {{system}} } } ],
              "taxonomies": []
            }
            """;

        var result = await RespondWithAsync(body);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Exception!.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task GetDeltaAsync_NullCollection_FailsInsteadOfYieldingNull()
    {
        const string body = """
            { "items": null, "types": [], "languages": [], "taxonomies": [] }
            """;

        var result = await RespondWithAsync(body);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    private async Task<ISyncResult<ISyncDeltaResponse>> RespondWithAsync(string body)
    {
        _http.Expect(HttpMethod.Get, SyncUrl)
            .Respond(_ => WithContinuation(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                },
                "next-token"));

        return await CreateClient().GetDeltaAsync("token");
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
