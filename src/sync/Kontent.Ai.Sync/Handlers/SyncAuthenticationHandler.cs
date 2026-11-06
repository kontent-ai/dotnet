using System.Net.Http.Headers;
using Kontent.Ai.Common;
using Kontent.Ai.Sync.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Handlers;

/// <summary>
/// Delegating handler that authenticates Sync requests.
/// </summary>
internal sealed class SyncAuthenticationHandler(
    IOptionsAccessor<SyncOptions> optionsAccessor,
    ILogger<SyncAuthenticationHandler>? logger = null) : DelegatingHandler
{
    // Taken when the HTTP client is created, together with its base address, so the key always matches the API
    // the request goes to. A sync token belongs to the feed that issued it: switching between the preview and
    // production feeds needs a restart and a new initialization, not a reload. The key is read per request so it
    // can rotate.
    private readonly ApiMode _apiMode = optionsAccessor.Current.ApiMode;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = optionsAccessor.Current;
        var apiKey = _apiMode is ApiMode.Preview or ApiMode.Secure ? options.ApiKey : null;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = null;
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (logger is not null)
            {
                LoggerMessages.HttpAuthSet(logger, "Bearer", options.EnvironmentId);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
