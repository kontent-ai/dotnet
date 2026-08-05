using System.Runtime.CompilerServices;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery;

/// <summary>
/// Executes requests against the Kontent.ai Delivery API using query builders.
/// </summary>
public sealed class DeliveryClient : IDeliveryClient, IDisposable, IAsyncDisposable
{
    private readonly IDeliveryApi _deliveryApi;
    private readonly ContentItemMapper _contentItemMapper;
    private readonly IContentDeserializer _contentDeserializer;
    private readonly ITypeProvider _typeProvider;
    private readonly IDeliveryCacheManager? _cacheManager;
    private readonly ILogger<DeliveryClient>? _logger;
    private readonly IDeliveryOptionsAccessor? _optionsAccessor;
    private readonly IDisposable? _ownedResources;
    private int _disposeState;

    /// <summary>
    /// Initializes a new client for retrieving content of the specified environment.
    /// </summary>
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
    internal DeliveryClient(
        IDeliveryApi deliveryApi,
        ContentItemMapper contentItemMapper,
        IContentDeserializer contentDeserializer,
        ITypeProvider typeProvider,
        IDeliveryCacheManager? cacheManager = null,
        ILogger<DeliveryClient>? logger = null,
        IDeliveryOptionsAccessor? optionsAccessor = null,
        IDisposable? ownedResources = null)
    {
        _deliveryApi = deliveryApi;
        _contentItemMapper = contentItemMapper;
        _contentDeserializer = contentDeserializer;
        _typeProvider = typeProvider;
        _cacheManager = cacheManager;
        _logger = logger;
        _optionsAccessor = optionsAccessor;
        _ownedResources = ownedResources;
    }

    /// <inheritdoc/>
    public IItemQuery<T> GetItem<T>(string codename)
    {
        EnsureCodenameValid(codename);
        return new ItemQuery<T>(
            _deliveryApi,
            codename,
            _contentItemMapper,
            _contentDeserializer,
            GetEffectiveCacheManager(),
            GetDefaultRenditionPreset(),
            GetCustomAssetDomain(),
            _logger);
    }

    /// <inheritdoc/>
    public IDynamicItemQuery GetItem(string codename)
    {
        EnsureCodenameValid(codename);
        return new DynamicItemQuery(
            _deliveryApi,
            codename,
            _contentItemMapper,
            _contentDeserializer,
            GetDefaultRenditionPreset(),
            GetCustomAssetDomain(),
            _logger);
    }

    /// <inheritdoc/>
    public IItemsQuery<T> GetItems<T>() => new ItemsQuery<T>(
        _deliveryApi,
        _contentItemMapper,
        _contentDeserializer,
        _typeProvider,
        GetEffectiveCacheManager(),
        GetDefaultRenditionPreset(),
        GetCustomAssetDomain(),
        _logger);

    /// <inheritdoc/>
    public IDynamicItemsQuery GetItems()
    {
        return new DynamicItemsQuery(
            _deliveryApi,
            _contentItemMapper,
            _contentDeserializer,
            _typeProvider,
            GetDefaultRenditionPreset(),
            GetCustomAssetDomain(),
            _logger);
    }

    /// <inheritdoc/>
    public IEnumerateItemsQuery<T> GetItemsFeed<T>() => new EnumerateItemsQuery<T>(
        _deliveryApi,
        _contentItemMapper,
        _typeProvider,
        GetDefaultRenditionPreset(),
        GetCustomAssetDomain(),
        _logger);

    /// <inheritdoc/>
    public IDynamicEnumerateItemsQuery GetItemsFeed() => new DynamicEnumerateItemsQuery(
        _deliveryApi,
        _contentItemMapper,
        _typeProvider,
        GetDefaultRenditionPreset(),
        GetCustomAssetDomain(),
        _logger);

    /// <inheritdoc/>
    public ITypeQuery GetType(string codename)
    {
        EnsureCodenameValid(codename);
        return new TypeQuery(_deliveryApi, codename, GetEffectiveCacheManager(), _logger);
    }

    /// <inheritdoc/>
    public ITypesQuery GetTypes() => new TypesQuery(_deliveryApi, GetEffectiveCacheManager(), _logger);

    /// <inheritdoc/>
    public ITypeElementQuery GetContentElement(string contentTypeCodename, string contentElementCodename)
    {
        EnsureCodenameValid(contentTypeCodename);
        EnsureCodenameValid(contentElementCodename);
        return new TypeElementQuery(_deliveryApi, contentTypeCodename, contentElementCodename, _logger);
    }

    /// <inheritdoc/>
    public ITaxonomyQuery GetTaxonomy(string codename)
    {
        EnsureCodenameValid(codename);
        return new TaxonomyQuery(_deliveryApi, codename, GetEffectiveCacheManager(), _logger);
    }

    /// <inheritdoc/>
    public ITaxonomiesQuery GetTaxonomies() => new TaxonomiesQuery(_deliveryApi, GetEffectiveCacheManager(), _logger);

    /// <inheritdoc/>
    public ILanguagesQuery GetLanguages() => new LanguagesQuery(_deliveryApi, _logger);

    /// <inheritdoc/>
    public IItemUsedInQuery GetItemUsedIn(string codename)
    {
        EnsureCodenameValid(codename);
        return new ItemUsedInQuery(_deliveryApi, codename, _logger);
    }

    /// <inheritdoc/>
    public IAssetUsedInQuery GetAssetUsedIn(string codename)
    {
        EnsureCodenameValid(codename);
        return new AssetUsedInQuery(_deliveryApi, codename, _logger);
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

        _ownedResources?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        if (_ownedResources is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _ownedResources?.Dispose();
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
        => IsPreviewApiEnabled() ? null : _cacheManager;

    private string? GetDefaultRenditionPreset()
        => _optionsAccessor?.Current.DefaultRenditionPreset;

    private Uri? GetCustomAssetDomain()
    {
        var domain = _optionsAccessor?.Current.CustomAssetDomain;
        return string.IsNullOrWhiteSpace(domain) ? null : new Uri(domain, UriKind.Absolute);
    }

    private bool IsPreviewApiEnabled()
        => _optionsAccessor?.Current.UsePreviewApi ?? false;
}
