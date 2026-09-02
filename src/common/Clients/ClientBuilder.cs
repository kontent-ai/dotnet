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
    /// the builder is configured, so it counts whatever the consumer chained after registration.
    /// </summary>
    internal Action<ResiliencePipelineBuilder<HttpResponseMessage>>? Resilience { get; private set; }

    protected void SetResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Resilience = configure;
    }
}
