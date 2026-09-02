// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Common.Clients;

/// <summary>
/// What every SDK's client builder is made of. The product's public builder interface is implemented
/// over this; the members are the two Microsoft builders the registration already produced, so every
/// options and HTTP client extension the platform ships is available to the consumer without the SDK
/// wrapping it.
/// </summary>
internal abstract class ClientBuilder<TOptions>(string name, IServiceCollection services, OptionsBuilder<TOptions> options)
    where TOptions : class
{
    public string Name { get; } = name;

    public IServiceCollection Services { get; } = services;

    public OptionsBuilder<TOptions> Options { get; } = options;

    /// <summary>
    /// Assigned by the transport registration before the builder reaches the consumer.
    /// </summary>
    public IHttpClientBuilder HttpClient { get; internal set; } = null!;

    /// <summary>
    /// The consumer's replacement pipeline, if any. Read when the HTTP client is first created, not when
    /// the builder is configured, so it counts whatever the consumer chained after registration. A holder
    /// of its own so the transport's closures capture it and not the builder, which would keep the service
    /// collection reachable for as long as the handler chain lives.
    /// </summary>
    internal ResilienceOverride Resilience { get; } = new();

    protected void SetResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Resilience.Configure = configure;
    }
}

/// <summary>
/// The slot <see cref="ClientBuilder{TOptions}.Resilience"/> is; see there.
/// </summary>
internal sealed class ResilienceOverride
{
    public Action<ResiliencePipelineBuilder<HttpResponseMessage>>? Configure { get; set; }
}
