using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Configures one Delivery client while it is being registered - the argument to
/// <c>services.AddDeliveryClient(...)</c> and to <see cref="DeliveryClient.Create(Action{IDeliveryClientBuilder})"/>.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's own setup has already run when the builder is handed over, so everything configured here
/// applies on top of it: <see cref="Options"/> is the client's <see cref="OptionsBuilder{TOptions}"/>
/// (<c>Configure</c>, <c>Bind</c>, <c>BindConfiguration</c>, <c>Validate</c> and the rest), and
/// <see cref="IDeliveryClientBuilder.HttpClient"/> is the named HTTP client the transport is built on, open to every
/// <c>Microsoft.Extensions.Http</c> extension.
/// </para>
/// <para>
/// <see cref="Services"/> and <see cref="Name"/> are what an extension package needs to attach something
/// to this client under its name - the caching package's <c>UseMemoryCache</c> and <c>UseHybridCache</c>
/// are built on them, as is registering a custom <c>ITypeProvider</c>.
/// </para>
/// </remarks>
public interface IDeliveryClientBuilder
{
    /// <summary>The client's name; the key it is registered under.</summary>
    string Name { get; }

    /// <summary>The service collection the client is being registered in.</summary>
    IServiceCollection Services { get; }

    /// <summary>The client's options.</summary>
    OptionsBuilder<DeliveryOptions> Options { get; }

    /// <summary>The named HTTP client the transport is built on.</summary>
    IHttpClientBuilder HttpClient { get; }

    /// <summary>
    /// Replaces the default resilience pipeline. Has no effect when <see cref="DeliveryOptions.EnableResilience"/>
    /// is <c>false</c>. With a pipeline of your own installed, <see cref="System.Net.Http.HttpClient.Timeout"/>'s 100-second default
    /// bounds the call unless <see cref="DeliveryOptions.Timeout"/> says otherwise, since only the SDK's pipeline
    /// is known to bound each attempt.
    /// </summary>
    IDeliveryClientBuilder ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure);
}
