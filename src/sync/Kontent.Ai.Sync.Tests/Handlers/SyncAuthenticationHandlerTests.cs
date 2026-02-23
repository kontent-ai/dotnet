using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Handlers;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Tests.Handlers;

public class SyncAuthenticationHandlerTests
{
    private const string EnvironmentId = "12345678-1234-1234-1234-123456789012";

    [Fact]
    public async Task SendAsync_WithPreviewApiKey_AddsAuthorizationHeader()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Preview,
            ApiKey = "preview-key"
        };

        var handler = CreateHandler(options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync");

        _ = await InvokeSendAsync(handler, request);

        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("preview-key");
    }

    [Fact]
    public async Task SendAsync_WhenApiKeyBecomesEmpty_ClearsAuthorizationHeader()
    {
        var withKey = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Preview,
            ApiKey = "preview-key"
        };

        var withoutKey = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Public
        };

        var monitor = new TestOptionsMonitor<SyncOptions>(withKey);
        var handler = new SyncAuthenticationHandler(monitor)
        {
            InnerHandler = new TestHandler()
        };

        var firstRequest = new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync");
        _ = await InvokeSendAsync(handler, firstRequest);

        firstRequest.Headers.Authorization.Should().NotBeNull();

        monitor.ChangeCurrentValue(withoutKey);

        var secondRequest = new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync");
        secondRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale-key");

        _ = await InvokeSendAsync(handler, secondRequest);

        secondRequest.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithTrustedDeliveryUrl_RewritesToConfiguredHost()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Preview,
            ApiKey = "preview-key",
            PreviewEndpoint = "https://preview.example.com"
        };

        var handler = CreateHandler(options);
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync?limit=5");

        _ = await InvokeSendAsync(handler, request);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.Host.Should().Be("preview.example.com");
        request.RequestUri.AbsolutePath.Should().Be($"/v2/{EnvironmentId}/sync");
        request.RequestUri.Query.Should().Be("?limit=5");
        request.Headers.Authorization.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_WithExternalUrl_DoesNotLeakAuthorizationOrInjectEnvironment()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Preview,
            ApiKey = "preview-key"
        };

        var handler = CreateHandler(options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://external-service.example/webhook");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale-sdk-key");

        _ = await InvokeSendAsync(handler, request);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.Host.Should().Be("external-service.example");
        request.RequestUri.AbsolutePath.Should().Be("/webhook");
        request.RequestUri.AbsolutePath.Should().NotContain(EnvironmentId);
        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithSecureApiKey_AddsAuthorizationHeader()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Secure,
            ApiKey = "secure-key"
        };

        var handler = CreateHandler(options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://deliver.kontent.ai/v2/sync");

        _ = await InvokeSendAsync(handler, request);

        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("secure-key");
    }

    [Fact]
    public async Task SendAsync_WithPreviewDeliveryUrl_IsTrusted()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Preview,
            ApiKey = "preview-key"
        };

        var handler = CreateHandler(options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://preview-deliver.kontent.ai/v2/sync");

        _ = await InvokeSendAsync(handler, request);

        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task SendAsync_DoesNotModifyPath()
    {
        var options = new SyncOptions
        {
            EnvironmentId = EnvironmentId,
            ApiMode = ApiMode.Public
        };

        var handler = CreateHandler(options);
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://deliver.kontent.ai/v2/{EnvironmentId}/sync");

        _ = await InvokeSendAsync(handler, request);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.AbsolutePath.Should().Be($"/v2/{EnvironmentId}/sync");
    }

    private static SyncAuthenticationHandler CreateHandler(SyncOptions options)
    {
        var monitor = new TestOptionsMonitor<SyncOptions>(options);
        return new SyncAuthenticationHandler(monitor)
        {
            InnerHandler = new TestHandler()
        };
    }

    private static async Task<HttpResponseMessage> InvokeSendAsync(SyncAuthenticationHandler handler, HttpRequestMessage request)
    {
        var sendAsyncMethod = typeof(SyncAuthenticationHandler)
            .GetMethod("SendAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("SendAsync method not found");

        var result = sendAsyncMethod.Invoke(handler, [request, CancellationToken.None]) as Task<HttpResponseMessage>
            ?? throw new InvalidOperationException("SendAsync returned null");

        return await result;
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        private TOptions _currentValue = currentValue;
        private readonly Dictionary<string, TOptions> _namedOptions = [];

        public TOptions CurrentValue => _currentValue;

        public TOptions Get(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return _currentValue;
            }

            return _namedOptions.TryGetValue(name, out var options)
                ? options
                : _currentValue;
        }

        public IDisposable OnChange(Action<TOptions, string> listener) => new EmptyDisposable();

        public void ChangeCurrentValue(TOptions newValue) => _currentValue = newValue;

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
