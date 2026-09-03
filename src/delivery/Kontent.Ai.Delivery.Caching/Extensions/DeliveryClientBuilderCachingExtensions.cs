using System.ComponentModel.DataAnnotations;
using Kontent.Ai.Common;
using Kontent.Ai.Delivery.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Attaches a cache to the Delivery client being registered. Each method registers the cache manager under
/// the client's name, so it works the same inside <c>services.AddDeliveryClient(...)</c> and inside
/// <c>DeliveryClient.Create(...)</c>; a later call for the same client replaces the earlier one.
/// </summary>
public static class DeliveryClientBuilderCachingExtensions
{
    /// <summary>
    /// Caches responses in memory, through <see cref="IMemoryCache"/>.
    /// </summary>
    /// <param name="builder">The client being registered.</param>
    /// <param name="configure">Configures the cache; validated at registration.</param>
    public static IDeliveryClientBuilder UseMemoryCache(this IDeliveryClientBuilder builder, Action<DeliveryCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var cacheOptions = CreateCacheOptions(builder.Name, configure);
        return UseMemoryCacheCore(builder, _ => cacheOptions);
    }

    /// <summary>
    /// Caches responses in memory, with the cache configured from services in the container. Validated when
    /// the client is first resolved.
    /// </summary>
    /// <param name="builder">The client being registered.</param>
    /// <param name="configure">Configures the cache with access to the <see cref="IServiceProvider"/>.</param>
    public static IDeliveryClientBuilder UseMemoryCache(this IDeliveryClientBuilder builder, Action<IServiceProvider, DeliveryCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return UseMemoryCacheCore(builder, sp => CreateCacheOptions(builder.Name, options => configure(sp, options)));
    }

    /// <summary>
    /// Caches responses in memory backed by the registered <see cref="IDistributedCache"/>, through a hybrid
    /// cache. Register the distributed cache on <see cref="IDeliveryClientBuilder.Services"/> the usual way,
    /// e.g. <c>AddStackExchangeRedisCache</c>; a FusionCache backplane registered there is picked up too.
    /// </summary>
    /// <param name="builder">The client being registered.</param>
    /// <param name="configure">Configures the cache; validated at registration.</param>
    public static IDeliveryClientBuilder UseHybridCache(this IDeliveryClientBuilder builder, Action<DeliveryCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var cacheOptions = CreateCacheOptions(builder.Name, configure);
        return UseHybridCacheCore(builder, _ => cacheOptions);
    }

    /// <summary>
    /// Caches responses in memory backed by the registered <see cref="IDistributedCache"/>, with the cache
    /// configured from services in the container. Validated when the client is first resolved.
    /// </summary>
    /// <param name="builder">The client being registered.</param>
    /// <param name="configure">Configures the cache with access to the <see cref="IServiceProvider"/>.</param>
    public static IDeliveryClientBuilder UseHybridCache(this IDeliveryClientBuilder builder, Action<IServiceProvider, DeliveryCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return UseHybridCacheCore(builder, sp => CreateCacheOptions(builder.Name, options => configure(sp, options)));
    }

    /// <summary>
    /// Caches responses through a cache manager of your own.
    /// </summary>
    /// <param name="builder">The client being registered.</param>
    /// <param name="createCacheManager">Creates the cache manager when the client is first resolved.</param>
    public static IDeliveryClientBuilder UseCacheManager(this IDeliveryClientBuilder builder, Func<IServiceProvider, IDeliveryCacheManager> createCacheManager)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(createCacheManager);

        return RegisterCacheManager(builder, createCacheManager);
    }

    private static IDeliveryClientBuilder UseMemoryCacheCore(IDeliveryClientBuilder builder, Func<IServiceProvider, DeliveryCacheOptions> cacheOptionsFactory)
    {
        // Register IMemoryCache if not already registered (shared across all clients)
        builder.Services.AddMemoryCache();

        return RegisterCacheManager(builder, sp => new MemoryCacheManager(
            sp.GetRequiredService<IMemoryCache>(),
            cacheOptionsFactory(sp),
            sp.GetService<ILogger<MemoryCacheManager>>(),
            EnvironmentIdOf(sp, builder.Name),
            sp.GetService<ILogger<FusionCache>>()));
    }

    private static IDeliveryClientBuilder UseHybridCacheCore(IDeliveryClientBuilder builder, Func<IServiceProvider, DeliveryCacheOptions> cacheOptionsFactory)
        => RegisterCacheManager(builder, sp => new HybridCacheManager(
            sp.GetRequiredService<IDistributedCache>(),
            cacheOptionsFactory(sp),
            logger: sp.GetService<ILogger<HybridCacheManager>>(),
            // Registered by the consumer the usual FusionCache way, e.g.
            // services.AddFusionCacheStackExchangeRedisBackplane(...).
            backplane: sp.GetService<IFusionCacheBackplane>(),
            environmentId: EnvironmentIdOf(sp, builder.Name),
            // FusionCache's own diagnostics: a distributed cache it worked around, a backplane publish
            // that failed, a background refresh that threw. Without it those are silent.
            fusionCacheLogger: sp.GetService<ILogger<FusionCache>>()));

    private static IDeliveryClientBuilder RegisterCacheManager(IDeliveryClientBuilder builder, Func<IServiceProvider, IDeliveryCacheManager> createCacheManager)
    {
        RemoveExistingCacheManagerRegistration(builder.Services, builder.Name);
        builder.Services.AddKeyedSingleton<IDeliveryCacheManager>(builder.Name, (sp, _) => createCacheManager(sp));
        return builder;
    }

    private static void RemoveExistingCacheManagerRegistration(IServiceCollection services, string clientName)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(IDeliveryCacheManager) &&
                Equals(descriptor.ServiceKey, clientName))
            {
                services.RemoveAt(i);
            }
        }
    }

    private static DeliveryCacheOptions CreateCacheOptions(string clientName, Action<DeliveryCacheOptions>? configure)
    {
        var cacheOptions = new DeliveryCacheOptions();
        configure?.Invoke(cacheOptions);
        cacheOptions.KeyPrefix = ResolveCacheKeyPrefix(clientName, cacheOptions.KeyPrefix);

        Validator.ValidateObject(cacheOptions, new ValidationContext(cacheOptions), validateAllProperties: true);
        return cacheOptions;
    }

    // The environment the cached content actually came from, so entries cannot be served to a client
    // pointing somewhere else - see FusionCacheManager.ComposeKeyPrefix.
    private static string EnvironmentIdOf(IServiceProvider sp, string clientName) =>
        sp.GetRequiredService<IOptionsMonitor<DeliveryOptions>>().Get(clientName).EnvironmentId;

    private static string ResolveCacheKeyPrefix(string clientName, string? keyPrefix)
        => keyPrefix ?? (clientName == NamedClients.Default ? string.Empty : clientName);
}
