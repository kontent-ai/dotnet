using Kontent.Ai.Delivery.Abstractions;
using ZiggyCreatures.Caching.Fusion;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Caching-package extensions for <see cref="DeliveryCacheOptions"/>.
/// </summary>
public static class DeliveryCacheOptionsExtensions
{
    /// <summary>
    /// Configures the underlying FusionCache instance, with the options typed.
    /// </summary>
    /// <remarks>
    /// <see cref="DeliveryCacheOptions.ConfigureFusionCacheOptions"/> is typed as <see cref="object"/>
    /// because it lives in <c>Kontent.Ai.Delivery.Abstractions</c>, which references no packages at all
    /// and should not start referencing FusionCache to expose one escape hatch. This package already
    /// references it, so the cast belongs here rather than in every caller.
    /// </remarks>
    /// <param name="options">The cache options to configure.</param>
    /// <param name="configure">Receives the <see cref="FusionCacheOptions"/> after the SDK's defaults are applied.</param>
    /// <returns>The same instance, for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDeliveryHybridCache(options => options
    ///     .ConfigureFusionCache(fusion => fusion.DefaultEntryOptions.AllowBackgroundBackplaneOperations = true));
    /// </code>
    /// </example>
    public static DeliveryCacheOptions ConfigureFusionCache(
        this DeliveryCacheOptions options,
        Action<FusionCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        options.ConfigureFusionCacheOptions = fusionOptions => configure((FusionCacheOptions)fusionOptions);

        return options;
    }
}
