using System.Net.Http.Headers;
using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Handlers;

/// <summary>
/// Delegating handler that injects authentication header and environment ID into Sync requests.
/// </summary>
internal sealed class SyncAuthenticationHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<SyncOptions> _monitor;
    private readonly string? _name;
    private readonly ILogger<SyncAuthenticationHandler>? _logger;

    /// <summary>
    /// Initializes a new instance using default options.
    /// </summary>
    /// <param name="monitor">Options monitor.</param>
    /// <param name="logger">Optional logger.</param>
    public SyncAuthenticationHandler(IOptionsMonitor<SyncOptions> monitor, ILogger<SyncAuthenticationHandler>? logger = null)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance using named options.
    /// </summary>
    /// <param name="monitor">Options monitor.</param>
    /// <param name="optionsName">Name of options to resolve.</param>
    /// <param name="logger">Optional logger.</param>
    public SyncAuthenticationHandler(IOptionsMonitor<SyncOptions> monitor, string optionsName, ILogger<SyncAuthenticationHandler>? logger = null)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsName);
        _name = optionsName;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _name is null ? _monitor.CurrentValue : _monitor.Get(_name);
        var baseUri = new Uri(options.GetBaseUrl().TrimEnd('/'), UriKind.Absolute);
        var isTrustedSyncRequest = request.RequestUri is null ||
                                   !request.RequestUri.IsAbsoluteUri ||
                                   ShouldRewriteUri(request.RequestUri, baseUri);

        if (!isTrustedSyncRequest)
        {
            request.Headers.Authorization = null;
            if (_logger is not null)
            {
                LoggerMessages.HttpAuthCleared(_logger);
            }
            return base.SendAsync(request, cancellationToken);
        }

        if (request.RequestUri is null)
        {
            request.RequestUri = baseUri;
        }
        else if (!request.RequestUri.IsAbsoluteUri)
        {
            request.RequestUri = new Uri(baseUri, request.RequestUri);
        }
        else if (ShouldRewriteUri(request.RequestUri, baseUri))
        {
            var originalHost = request.RequestUri.Host;
            var uriBuilder = new UriBuilder(request.RequestUri)
            {
                Scheme = baseUri.Scheme,
                Host = baseUri.Host,
                Port = baseUri.IsDefaultPort ? -1 : baseUri.Port
            };
            request.RequestUri = uriBuilder.Uri;

            if (_logger is not null && !originalHost.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                LoggerMessages.HttpEndpointRewritten(_logger, originalHost, baseUri.Host);
            }
        }

        var apiKey = options.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (_logger is not null)
            {
                LoggerMessages.HttpAuthSet(_logger, "Bearer", options.EnvironmentId);
            }
        }
        else
        {
            request.Headers.Authorization = null;
            if (_logger is not null)
            {
                LoggerMessages.HttpAuthCleared(_logger);
            }
        }

        var env = options.EnvironmentId?.Trim('/');
        if (!string.IsNullOrWhiteSpace(env) && request.RequestUri is not null)
        {
            var uri = request.RequestUri;
            var path = uri.AbsolutePath;
            var envPrefix = "/" + env;

            if (!path.Equals(envPrefix, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(envPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                var uriBuilder = new UriBuilder(uri)
                {
                    Path = envPrefix + path
                };
                request.RequestUri = uriBuilder.Uri;

                if (_logger is not null)
                {
                    LoggerMessages.HttpEnvironmentIdInjected(_logger, env);
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool ShouldRewriteUri(Uri requestUri, Uri configuredBase)
    {
        var host = requestUri.Host;

        if (host.Equals(configuredBase.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (host.Equals("deliver.kontent.ai", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("preview-deliver.kontent.ai", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
