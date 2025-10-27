using Kontent.Ai.Sync.Abstractions;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Handlers;

/// <summary>
/// HTTP message handler that adds authentication headers to Sync API requests.
/// </summary>
internal sealed class SyncAuthenticationHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<SyncOptions> _optionsMonitor;
    private readonly string? _optionsName;

    /// <summary>
    /// Initializes a new instance using default options.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor.</param>
    public SyncAuthenticationHandler(IOptionsMonitor<SyncOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _optionsName = null;
    }

    /// <summary>
    /// Initializes a new instance using named options.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor.</param>
    /// <param name="optionsName">The name of the options to use.</param>
    public SyncAuthenticationHandler(IOptionsMonitor<SyncOptions> optionsMonitor, string optionsName)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _optionsName = optionsName ?? throw new ArgumentNullException(nameof(optionsName));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _optionsName is null
            ? _optionsMonitor.CurrentValue
            : _optionsMonitor.Get(_optionsName);

        // Add Preview API authentication if configured
        if (options.UsePreviewApi && !string.IsNullOrWhiteSpace(options.PreviewApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {options.PreviewApiKey}");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
