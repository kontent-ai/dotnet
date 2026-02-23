using System.Net.Http.Headers;
using Kontent.Ai.Sync.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Handlers;

/// <summary>
/// Delegating handler that injects authentication header and rewrites hosts for Sync requests.
/// </summary>
/// <remarks>
/// Initializes a new instance using default or named options.
/// </remarks>
/// <param name="monitor">Options monitor.</param>
/// <param name="optionsName">Name of options to resolve, or <c>null</c> for default.</param>
/// <param name="logger">Optional logger.</param>
internal sealed class SyncAuthenticationHandler(
    IOptionsMonitor<SyncOptions> monitor,
    string? optionsName = null,
    ILogger<SyncAuthenticationHandler>? logger = null) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = optionsName is null ? monitor.CurrentValue : monitor.Get(optionsName);
        var baseUri = new Uri(options.GetBaseUrl().TrimEnd('/'), UriKind.Absolute);

        if (!IsTrustedHost(request.RequestUri, baseUri))
        {
            ClearAuthentication(request);
            return base.SendAsync(request, cancellationToken);
        }

        RewriteHostIfNeeded(request, baseUri);
        SetAuthentication(request, options);

        return base.SendAsync(request, cancellationToken);
    }

    private void RewriteHostIfNeeded(HttpRequestMessage request, Uri baseUri)
    {
        if (request.RequestUri is null)
        {
            request.RequestUri = baseUri;
        }
        else if (!request.RequestUri.IsAbsoluteUri)
        {
            request.RequestUri = new Uri(baseUri, request.RequestUri);
        }
        else
        {
            var originalHost = request.RequestUri.Host;
            var uriBuilder = new UriBuilder(request.RequestUri)
            {
                Scheme = baseUri.Scheme,
                Host = baseUri.Host,
                Port = baseUri.IsDefaultPort ? -1 : baseUri.Port
            };
            request.RequestUri = uriBuilder.Uri;

            if (logger is not null && !originalHost.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                LoggerMessages.HttpEndpointRewritten(logger, originalHost, baseUri.Host);
            }
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

    private static bool IsTrustedHost(Uri? requestUri, Uri configuredBase) =>
        requestUri is null ||
        !requestUri.IsAbsoluteUri ||
        requestUri.Host.Equals(configuredBase.Host, StringComparison.OrdinalIgnoreCase) ||
        requestUri.Host.Equals("deliver.kontent.ai", StringComparison.OrdinalIgnoreCase) ||
        requestUri.Host.Equals("preview-deliver.kontent.ai", StringComparison.OrdinalIgnoreCase);
}
