using Kontent.Ai.Common.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Sync.Configuration;

internal sealed class SyncClientBuilder(string name, IServiceCollection services, OptionsBuilder<SyncOptions> options)
    : ClientBuilder<SyncOptions>(name, services, options), ISyncClientBuilder
{
    public ISyncClientBuilder ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
    {
        SetResilience(configure);
        return this;
    }
}
