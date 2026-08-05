using System.Runtime.CompilerServices;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Executes requests against the Kontent.ai Delivery API using query builders.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DeliveryClient"/> class for retrieving content of the specified environment.
/// </remarks>
/// <param name="deliveryApi">The Refit-generated API client.</param>
/// <param name="contentItemMapper">The content item mapper for element hydration.</param>
/// <param name="contentDeserializer">The content deserializer for JSON to object conversion.</param>
/// <param name="typeProvider">The type provider for content type to CLR type mapping.</param>
/// <param name="cacheManager">Optional cache manager for caching API responses (injected when EnableCaching is true).</param>
/// <param name="logger">Optional logger for diagnostic output.</param>
/// <param name="optionsAccessor">Provides the effective <see cref="DeliveryOptions"/> at runtime (monitor- or snapshot-backed).</param>
/// <param name="ownedResources">
/// Resources whose lifetime this client is responsible for, or <c>null</c> when something else owns them.
/// A container passes <c>null</c> - it owns the transport and disposes the client itself.
/// <see cref="DeliveryClientBuilder"/> passes the private service provider it built, so disposing the
/// client tears down that provider and everything registered in it.
/// </param>
internal sealed class DeliveryClient(
    IDeliveryApi deliveryApi,
    ContentItemMapper contentItemMapper,
    IContentDeserializer contentDeserializer,
    ITypeProvider typeProvider,
    IDeliveryCacheManager? cacheManager = null,
    ILogger<DeliveryClient>? logger = null,
    IDeliveryOptionsAccessor? optionsAccessor = null,
    IDisposable? ownedResources = null) : IDeliveryClient
{
    private int _disposeState;

    public IItemQuery<T> GetItem<T>(string codename)
    {
        EnsureCodenameValid(codename);
        return new ItemQuery<T>(
            deliveryApi,
            codename,
            contentItemMapper,
            contentDeserializer,
            GetEffectiveCacheManager(),
            GetDefaultRenditionPreset(),
            GetCustomAssetDomain(),
            logger);
    }

    public IDynamicItemQuery GetItem(string codename)
    {
        EnsureCodenameValid(codename);
        return new DynamicItemQuery(
            deliveryApi,
            codename,
            contentItemMapper,
            contentDeserializer,
            GetDefaultRenditionPreset(),
            GetCustomAssetDomain(),
            logger);
    }

    public IItemsQuery<T> GetItems<T>() => new ItemsQuery<T>(
        deliveryApi,
        contentItemMapper,
        contentDeserializer,
        typeProvider,
        GetEffectiveCacheManager(),
        GetDefaultRenditionPreset(),
        GetCustomAssetDomain(),
        logger);

    public IDynamicItemsQuery GetItems()
    {
        return new DynamicItemsQuery(
            deliveryApi,
            contentItemMapper,
            contentDeserializer,
            typeProvider,
            GetDefaultRenditionPreset(),
            GetCustomAssetDomain(),
            logger);
    }

    public IEnumerateItemsQuery<T> GetItemsFeed<T>() => new EnumerateItemsQuery<T>(
        deliveryApi,
        contentItemMapper,
        typeProvider,
        GetDefaultRenditionPreset(),
        GetCustomAssetDomain(),
        logger);

    public IDynamicEnumerateItemsQuery GetItemsFeed() => new DynamicEnumerateItemsQuery(
        deliveryApi,
        contentItemMapper,
        typeProvider,
        GetDefaultRenditionPreset(),
        GetCustomAssetDomain(),
        logger);

    public ITypeQuery GetType(string codename)
    {
        EnsureCodenameValid(codename);
        return new TypeQuery(deliveryApi, codename, GetEffectiveCacheManager(), logger);
    }

    public ITypesQuery GetTypes() => new TypesQuery(deliveryApi, GetEffectiveCacheManager(), logger);

    public ITypeElementQuery GetContentElement(string contentTypeCodename, string contentElementCodename)
    {
        EnsureCodenameValid(contentTypeCodename);
        EnsureCodenameValid(contentElementCodename);
        return new TypeElementQuery(deliveryApi, contentTypeCodename, contentElementCodename, logger);
    }

    public ITaxonomyQuery GetTaxonomy(string codename)
    {
        EnsureCodenameValid(codename);
        return new TaxonomyQuery(deliveryApi, codename, GetEffectiveCacheManager(), logger);
    }

    public ITaxonomiesQuery GetTaxonomies() => new TaxonomiesQuery(deliveryApi, GetEffectiveCacheManager(), logger);

    public ILanguagesQuery GetLanguages() => new LanguagesQuery(deliveryApi, logger);

    public IItemUsedInQuery GetItemUsedIn(string codename)
    {
        EnsureCodenameValid(codename);
        return new ItemUsedInQuery(deliveryApi, codename, logger);
    }

    public IAssetUsedInQuery GetAssetUsedIn(string codename)
    {
        EnsureCodenameValid(codename);
        return new AssetUsedInQuery(deliveryApi, codename, logger);
    }

    /// <summary>
    /// Releases what this client owns. Nothing, when a container supplied its dependencies.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        ownedResources?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        if (ownedResources is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            ownedResources?.Dispose();
        }
    }

    private static void EnsureCodenameValid(string? codename, [CallerArgumentExpression(nameof(codename))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(codename))
        {
            throw new ArgumentException($"Entered {parameterName} is not valid.", parameterName);
        }
    }

    private IDeliveryCacheManager? GetEffectiveCacheManager()
        => IsPreviewApiEnabled() ? null : cacheManager;

    private string? GetDefaultRenditionPreset()
        => optionsAccessor?.Current.DefaultRenditionPreset;

    private Uri? GetCustomAssetDomain()
    {
        var domain = optionsAccessor?.Current.CustomAssetDomain;
        return string.IsNullOrWhiteSpace(domain) ? null : new Uri(domain, UriKind.Absolute);
    }

    private bool IsPreviewApiEnabled()
        => optionsAccessor?.Current.UsePreviewApi ?? false;
}
