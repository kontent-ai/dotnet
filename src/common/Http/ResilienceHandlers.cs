// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Installs the resilience handler the way every SDK does: gated on the client's options, and built
/// only when the HTTP handler chain is - so a pipeline the consumer supplies after registration counts.
/// </summary>
internal static class ResilienceHandlers
{
    /// <param name="httpClientBuilder">The named HTTP client to install on.</param>
    /// <param name="handlerName">The resilience handler's name, unique per client.</param>
    /// <param name="clientName">The name the client's options are registered under.</param>
    /// <param name="isEnabled">Reads the option that switches resilience off.</param>
    /// <param name="configure">Builds the pipeline; runs when the HTTP handler chain is first built.</param>
    internal static void AddOptionsGated<TOptions>(
        IHttpClientBuilder httpClientBuilder,
        string handlerName,
        string clientName,
        Func<TOptions, bool> isEnabled,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
        where TOptions : class
    {
        httpClientBuilder.AddResilienceHandler(handlerName, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptionsMonitor<TOptions>>().Get(clientName);

            if (!isEnabled(options))
            {
                return;
            }

            configure(builder);
        });
    }
}
