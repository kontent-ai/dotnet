using Kontent.Ai.Common.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Management.Configuration;

internal sealed class ManagementClientBuilder(string name, IServiceCollection services, OptionsBuilder<ManagementOptions> options)
    : ClientBuilder<ManagementOptions>(name, services, options), IManagementClientBuilder
{
    /// <summary>
    /// Assigned by the transport registration before the builder reaches the consumer.
    /// </summary>
    public IHttpClientBuilder SubscriptionHttpClient { get; internal set; } = null!;

    public IManagementClientBuilder ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
    {
        SetResilience(configure);
        return this;
    }
}
