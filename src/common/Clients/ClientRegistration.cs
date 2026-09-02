// Shared source, compiled into each SDK assembly - see src/common/README.md.

// Refit resolves through each consuming project's global usings; see src/common/README.md.

using Kontent.Ai.Common.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Polly;

namespace Kontent.Ai.Common.Clients;

/// <summary>
/// What differs between the products' transports. Everything else about registering one is the same
/// sequence, in <see cref="ClientRegistration.AddTransport{TOptions, TApi}"/>.
/// </summary>
/// <param name="HttpClientName">The named HTTP client, unique per client and scope.</param>
/// <param name="ResilienceHandlerName">The resilience handler's name, unique per client and scope.</param>
/// <param name="BaseAddress">The scoped base address, read from the effective options.</param>
/// <param name="ResilienceEnabled">The option that switches resilience off.</param>
/// <param name="Ceiling">
/// The <see cref="HttpClient.Timeout"/>, from the options, whether the product's default pipeline is the
/// one installed, and <see cref="HttpClient"/>'s own default.
/// </param>
/// <param name="DefaultPipeline">The product's default resilience pipeline.</param>
/// <param name="AddHandlers">The product's delegating handlers, outermost first.</param>
internal sealed record TransportRecipe<TOptions>(
    string HttpClientName,
    string ResilienceHandlerName,
    Func<TOptions, Uri> BaseAddress,
    Func<TOptions, bool> ResilienceEnabled,
    Func<TOptions, bool, TimeSpan, TimeSpan> Ceiling,
    Action<ResiliencePipelineBuilder<HttpResponseMessage>> DefaultPipeline,
    Action<IHttpClientBuilder> AddHandlers)
    where TOptions : class;

/// <summary>
/// The registration sequence every SDK client goes through. The order is the contract: the duplicate
/// check first, the options before anything reads them, resilience outside the product's handlers so a
/// retry re-runs them, connection recycling last of the SDK's own steps - and the consumer's chain after
/// all of it, because the builder is handed back only once this has run.
/// </summary>
internal static class ClientRegistration
{
    /// <summary>
    /// Registers the client's options under <paramref name="name"/>, validated at startup, and returns the
    /// product's builder over them. For the default client the unnamed options resolve as well, as a copy
    /// of the named ones that follows their reloads, so whatever the consumer configures on the builder
    /// reaches both. The unnamed copy is validated on read but not at startup: the mirror builds it through
    /// the named factory, which already validates, and a second startup validation would report the one
    /// failure twice.
    /// </summary>
    internal static TBuilder AddClient<TOptions, TClient, TBuilder>(
        IServiceCollection services,
        string name,
        string clientDescription,
        string? httpClientName,
        Func<string, IServiceCollection, OptionsBuilder<TOptions>, TBuilder> createBuilder)
        where TOptions : class
        where TClient : class
        where TBuilder : ClientBuilder<TOptions>
    {
        ArgumentNullException.ThrowIfNull(services);
        NamedClients.ValidateName(name);
        KeyedClients.EnsureNotRegistered<TClient>(services, name, clientDescription, httpClientName);

        var options = services.AddOptions<TOptions>(name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (name == NamedClients.Default)
        {
            services.AddTransient<IConfigureOptions<TOptions>, DefaultClientOptionsMirror<TOptions>>();
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>, DefaultClientChangeTokenSource<TOptions>>();
            services.AddOptions<TOptions>().ValidateDataAnnotations();
        }

        return createBuilder(name, services, options);
    }

    /// <summary>
    /// Makes the unnamed options a copy of the default client's. It builds the named options through a
    /// factory of its own inside <see cref="Configure(string?, TOptions)"/>, never in its constructor: a
    /// configure registration that resolves the options monitor while the monitor is being built re-enters
    /// the options factory, and the container recurses without bound.
    /// </summary>
    private sealed class DefaultClientOptionsMirror<TOptions>(IServiceProvider serviceProvider) : IConfigureNamedOptions<TOptions>
        where TOptions : class
    {
        public void Configure(string? name, TOptions options)
        {
            if (name == Microsoft.Extensions.Options.Options.DefaultName)
            {
                var named = serviceProvider.GetRequiredService<IOptionsFactory<TOptions>>().Create(NamedClients.Default);
                OptionsCopier<TOptions>.Copy(named, options);
            }
        }

        public void Configure(TOptions options) => Configure(Microsoft.Extensions.Options.Options.DefaultName, options);
    }

    /// <summary>
    /// Forwards the default client's reload signal to the unnamed options. The options monitor invalidates
    /// a cached instance only when a change token source registered under that instance's name fires, and
    /// <c>BindConfiguration</c> registers its source under the client's name - so without this the mirror's
    /// copy would be rebuilt on the named side and served stale on the unnamed one. The sources are looked
    /// up inside <see cref="GetChangeToken"/>, never in the constructor, for the same reason the mirror
    /// resolves nothing in its own.
    /// </summary>
    private sealed class DefaultClientChangeTokenSource<TOptions>(IServiceProvider serviceProvider) : IOptionsChangeTokenSource<TOptions>
        where TOptions : class
    {
        public string? Name => Microsoft.Extensions.Options.Options.DefaultName;

        public IChangeToken GetChangeToken()
        {
            var tokens = serviceProvider.GetServices<IOptionsChangeTokenSource<TOptions>>()
                .Where(source => source.Name == NamedClients.Default)
                .Select(source => source.GetChangeToken())
                .ToArray();

            return new CompositeChangeToken(tokens);
        }
    }

    /// <summary>
    /// Registers one transport for <paramref name="builder"/>'s client: the keyed generated Refit client over
    /// a named HTTP client, its base address and ceiling, the options-gated resilience handler, the product's
    /// handlers, and connection recycling.
    /// </summary>
    internal static IHttpClientBuilder AddTransport<TOptions, TApi>(
        ClientBuilder<TOptions> builder,
        TransportRecipe<TOptions> recipe,
        RefitSettings refitSettings)
        where TOptions : class
        where TApi : class
    {
        var name = builder.Name;
        var resilience = builder.Resilience;

        var httpClientBuilder = builder.Services
            .AddKeyedRefitGeneratedClient<TApi>(name, refitSettings, recipe.HttpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name);
                httpClient.BaseAddress = recipe.BaseAddress(options);
                httpClient.Timeout = recipe.Ceiling(options, resilience.Configure is null, httpClient.Timeout);
            });

        ResilienceHandlers.AddOptionsGated<TOptions>(
            httpClientBuilder,
            recipe.ResilienceHandlerName,
            name,
            recipe.ResilienceEnabled,
            pipeline => (resilience.Configure ?? recipe.DefaultPipeline)(pipeline));

        recipe.AddHandlers(httpClientBuilder);
        HttpClientDefaults.ConfigureConnectionRecycling(httpClientBuilder);

        return httpClientBuilder;
    }

    /// <summary>
    /// Registers the client itself under <paramref name="name"/>, its factory once, and - for the default
    /// client - the unkeyed alias.
    /// </summary>
    internal static void AddClientServices<TClient, TFactoryService, TFactoryImplementation>(
        IServiceCollection services,
        string name,
        Func<IServiceProvider, object?, TClient> createClient)
        where TClient : class
        where TFactoryService : class
        where TFactoryImplementation : class, TFactoryService
    {
        services.AddKeyedSingleton(name, createClient);
        services.TryAddSingleton<TFactoryService, TFactoryImplementation>();

        if (name == NamedClients.Default)
        {
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<TClient>(NamedClients.Default));
        }
    }
}
