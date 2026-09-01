using System.Net.Http.Headers;
using Kontent.Ai.Common;
using Kontent.Ai.Sync.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Sync.Handlers;

/// <summary>
/// Delegating handler that injects authentication header and rewrites hosts for Sync requests.
/// </summary>
internal sealed class SyncAuthenticationHandler(
    IOptionsAccessor<SyncOptions> optionsAccessor,
    ILogger<SyncAuthenticationHandler>? logger = null) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = optionsAccessor.Current;
        var baseUri = new Uri(options.GetBaseUrl().TrimEnd('/'), UriKind.Absolute);

        // Absolute by now: HttpClient resolves the URI against BaseAddress before the handler chain runs.
        var requestUri = request.RequestUri!;

        if (!IsTrustedHost(requestUri, baseUri))
        {
            ClearAuthentication(request);
            return base.SendAsync(request, cancellationToken);
        }

        RewriteHost(request, requestUri, baseUri);
        SetAuthentication(request, options);

        return base.SendAsync(request, cancellationToken);
    }

    // An options reload can move the endpoint after the HttpClient captured its BaseAddress.
    private void RewriteHost(HttpRequestMessage request, Uri requestUri, Uri baseUri)
    {
        var originalHost = requestUri.Host;

        request.RequestUri = new UriBuilder(requestUri)
        {
            Scheme = baseUri.Scheme,
            Host = baseUri.Host,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port
        }.Uri;

        if (logger is not null && !originalHost.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            LoggerMessages.HttpEndpointRewritten(logger, originalHost, baseUri.Host);
        }
    }

    private void SetAuthentication(HttpRequestMessage request, SyncOptions options)
    {
        var apiKey = options.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (logger is not null)
            {
                LoggerMessages.HttpAuthSet(logger, "Bearer", options.EnvironmentId);
            }
        }
        else
        {
            ClearAuthentication(request);
        }
    }

    private void ClearAuthentication(HttpRequestMessage request)
    {
        request.Headers.Authorization = null;
        if (logger is not null)
        {
            LoggerMessages.HttpAuthCleared(logger);
        }
    }

    private static bool IsTrustedHost(Uri requestUri, Uri configuredBase) =>
        requestUri.Host.Equals(configuredBase.Host, StringComparison.OrdinalIgnoreCase) ||
        requestUri.Host.Equals("deliver.kontent.ai", StringComparison.OrdinalIgnoreCase) ||
        requestUri.Host.Equals("preview-deliver.kontent.ai", StringComparison.OrdinalIgnoreCase);
}
