using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.ContentItems.RichText.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kontent.Ai.AspNetCore.RichText;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering Kontent.ai rich-text services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IHtmlResolver"/> singleton the <c>&lt;rich-text&gt;</c> tag helper renders with.
    /// </summary>
    /// <remarks>
    /// Calling this method replaces any prior <see cref="IHtmlResolver"/> registration. The resolver is built lazily on
    /// first resolution; the <paramref name="configure"/> callback runs at that time. <see cref="RichTextExtensions.ToHtmlContentAsync"/>
    /// cannot see the container: pass it the registered resolver explicitly, or it uses the SDK's defaults.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback that receives the builder used to assemble the resolver.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddKontentRichText(
        this IServiceCollection services,
        Action<IHtmlResolverBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddKontentRichText(
            configure is null ? null : (_, builder) => configure(builder));
    }

    /// <summary>
    /// Registers the <see cref="IHtmlResolver"/> singleton the <c>&lt;rich-text&gt;</c> tag helper renders with,
    /// giving the configuration callback access to the application's <see cref="IServiceProvider"/>.
    /// </summary>
    /// <remarks>
    /// Calling this method replaces any prior <see cref="IHtmlResolver"/> registration. The resolver is built lazily on
    /// first resolution; the <paramref name="configure"/> callback runs at that time with the <em>root</em> service
    /// provider, so anything it captures must be singleton-safe. An application whose resolver needs request-scoped
    /// services registers a scoped <see cref="IHtmlResolver"/> itself instead. <see cref="RichTextExtensions.ToHtmlContentAsync"/>
    /// cannot see the container: pass it the registered resolver explicitly, or it uses the SDK's defaults.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback that receives the service provider and the builder used to assemble the resolver.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddKontentRichText(
        this IServiceCollection services,
        Action<IServiceProvider, IHtmlResolverBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IHtmlResolver>();
        services.AddSingleton<IHtmlResolver>(sp =>
        {
            var builder = new HtmlResolverBuilder();
            configure?.Invoke(sp, builder);
            return builder.Build();
        });
        return services;
    }
}
