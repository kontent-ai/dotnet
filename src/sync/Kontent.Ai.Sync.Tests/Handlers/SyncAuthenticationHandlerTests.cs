using System.Net.Http.Headers;
using AwesomeAssertions;
using Kontent.Ai.Common;
using Kontent.Ai.Sync.Handlers;

namespace Kontent.Ai.Sync.Tests.Handlers;

public class SyncAuthenticationHandlerTests
{
    private const string EnvironmentId = "12345678-1234-1234-1234-123456789012";
    private const string SyncUrl = $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync";

    [Theory]
    [InlineData(ApiMode.Preview)]
    [InlineData(ApiMode.Secure)]
    public async Task SendAsync_WithAKeyedMode_SendsTheKey(ApiMode mode)
    {
        var options = new MutableOptions(new SyncOptions { EnvironmentId = EnvironmentId, ApiMode = mode, ApiKey = "the-key" });

        var request = await SendOneAsync(options);

        request.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "the-key"));
    }

    [Fact]
    public async Task SendAsync_InPublicMode_SendsNoKey_EvenWhenOneIsSet()
    {
        var options = new MutableOptions(new SyncOptions { EnvironmentId = EnvironmentId, ApiKey = "unused-key" });

        var request = await SendOneAsync(options);

        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ARotatedKey_TakesEffectOnTheNextRequest()
    {
        var options = new MutableOptions(new SyncOptions { EnvironmentId = EnvironmentId, ApiMode = ApiMode.Preview, ApiKey = "old-key" });
        using var invoker = CreateInvoker(options);

        options.Current = new SyncOptions { EnvironmentId = EnvironmentId, ApiMode = ApiMode.Preview, ApiKey = "new-key" };
        var request = await SendOneAsync(invoker);

        request.Headers.Authorization!.Parameter.Should().Be("new-key");
    }

    // The base address is taken when the HTTP client is created, so the mode is too: a reloaded mode
    // must not start sending one API's key to the other API's host.
    [Fact]
    public async Task SendAsync_AReloadedMode_IsIgnoredUntilTheClientIsCreatedAgain()
    {
        var options = new MutableOptions(new SyncOptions { EnvironmentId = EnvironmentId });
        using var invoker = CreateInvoker(options);

        options.Current = new SyncOptions { EnvironmentId = EnvironmentId, ApiMode = ApiMode.Preview, ApiKey = "preview-key" };
        var request = await SendOneAsync(invoker);

        request.Headers.Authorization.Should().BeNull();
        request.RequestUri.Should().Be(new Uri(SyncUrl));
    }

    // A retry sends the same request message again, so a header from the first attempt is still on it.
    [Fact]
    public async Task SendAsync_WhenTheKeyIsRemoved_ClearsAHeaderFromAnEarlierAttempt()
    {
        var options = new MutableOptions(new SyncOptions { EnvironmentId = EnvironmentId, ApiMode = ApiMode.Preview, ApiKey = "old-key" });
        using var invoker = CreateInvoker(options);
        using var request = new HttpRequestMessage(HttpMethod.Get, SyncUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "old-key");

        options.Current = new SyncOptions { EnvironmentId = EnvironmentId, ApiMode = ApiMode.Preview, ApiKey = null };
        using var _ = await invoker.SendAsync(request, CancellationToken.None);

        request.Headers.Authorization.Should().BeNull();
    }

    private static async Task<HttpRequestMessage> SendOneAsync(MutableOptions options)
    {
        using var invoker = CreateInvoker(options);
        return await SendOneAsync(invoker);
    }

    private static async Task<HttpRequestMessage> SendOneAsync(HttpMessageInvoker invoker)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, SyncUrl);
        using var _ = await invoker.SendAsync(request, CancellationToken.None);
        return request;
    }

    private static HttpMessageInvoker CreateInvoker(MutableOptions options) =>
        new(new SyncAuthenticationHandler(options) { InnerHandler = new OkHandler() });

    private sealed class MutableOptions(SyncOptions initial) : IOptionsAccessor<SyncOptions>
    {
        public SyncOptions Current { get; set; } = initial;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
