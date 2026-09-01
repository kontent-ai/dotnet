using System.Net.Http.Headers;
using Kontent.Ai.Common;
using Kontent.Ai.Management.Configuration;

namespace Kontent.Ai.Management.Handlers;

/// <summary>
/// Attaches the bearer-token <c>Authorization</c> header to every outgoing Management API request. Reads the key
/// through <see cref="IOptionsAccessor{ManagementOptions}"/> so the DI path (monitor-backed, reactive) and the standalone
/// path (snapshot) share the same handler implementation.
/// </summary>
internal sealed class ManagementAuthenticationHandler(IOptionsAccessor<ManagementOptions> optionsAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", optionsAccessor.Current.ApiKey);

        return base.SendAsync(request, cancellationToken);
    }
}
