using Kontent.Ai.Management.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace Kontent.Ai.Management;

/// <summary>
/// Configures one Management client while it is being registered - the argument to
/// <c>services.AddManagementClient(...)</c> and to <see cref="ManagementClient.Create(Action{IManagementClientBuilder})"/>.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's own setup has already run when the builder is handed over, so everything configured here
/// applies on top of it: <see cref="Options"/> is the client's <see cref="OptionsBuilder{TOptions}"/>
/// (<c>Configure</c>, <c>Bind</c>, <c>BindConfiguration</c>, <c>Validate</c> and the rest), and the two
/// HTTP client builders are the named clients the environment-scoped and subscription-scoped transports
/// are built on, open to every <c>Microsoft.Extensions.Http</c> extension.
/// </para>
/// <para>
/// <see cref="Services"/> and <see cref="Name"/> are what an extension package needs to attach something
/// to this client under its name.
/// </para>
/// </remarks>
public interface IManagementClientBuilder
{
    /// <summary>The client's name; the key it is registered under.</summary>
    string Name { get; }

    /// <summary>The service collection the client is being registered in.</summary>
    IServiceCollection Services { get; }

    /// <summary>The client's options.</summary>
    OptionsBuilder<ManagementOptions> Options { get; }

    /// <summary>The named HTTP client the environment-scoped transport is built on.</summary>
    IHttpClientBuilder HttpClient { get; }

    /// <summary>The named HTTP client the subscription-scoped transport is built on.</summary>
    IHttpClientBuilder SubscriptionHttpClient { get; }

    /// <summary>
    /// Replaces the default resilience pipeline on both transports. Has no effect when
    /// <see cref="ManagementOptions.EnableResilience"/> is <c>false</c>.
    /// </summary>
    IManagementClientBuilder ConfigureResilience(Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure);
}
