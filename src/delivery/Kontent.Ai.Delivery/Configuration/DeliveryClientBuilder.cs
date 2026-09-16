using Kontent.Ai.Common.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Delivery.Configuration;

internal sealed class DeliveryClientBuilder(string name, IServiceCollection services, OptionsBuilder<DeliveryOptions> options)
    : ClientBuilder<DeliveryOptions>(name, services, options), IDeliveryClientBuilder
{
    public IDeliveryClientBuilder TuneRetry(Action<HttpRetryStrategyOptions> configure)
    {
        AddRetryTuning(configure);
        return this;
    }

    public IDeliveryClientBuilder ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure)
    {
        SetResilience(configure);
        return this;
    }
}
