namespace Kontent.Ai.Delivery.ContentItems.RichText.Resolution;

/// <summary>
/// Fluent builder for configuring HTML resolvers for rich text content.
/// </summary>
/// <remarks>
/// <para>
/// The SDK automatically provides sensible defaults for text nodes, HTML elements, and inline images.
/// You only need to configure resolvers for app-specific content:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="IContentItemLink"/> - Links to other content items (requires URL generation logic)</description></item>
///   <item><description><see cref="IEmbeddedContent"/> - Embedded content items/components (requires rendering logic)</description></item>
/// </list>
/// <para>
/// Custom resolvers override the built-in defaults.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var resolver = new HtmlResolverBuilder()
///     .WithContentItemLinkResolver(DefaultResolvers.UrlPatternResolver(new Dictionary&lt;string, string&gt;
///     {
///         ["article"] = "/articles/{urlslug}",
///         ["product"] = "/products/{urlslug}"
///     }))
///     .WithContentResolver("tweet", (content, ctx) =>
///         $"&lt;div class='tweet'&gt;{content.Name}&lt;/div&gt;")
///     .WithContentResolver("video", (content, ctx) =>
///         $"&lt;video src='{content.Content.Url}'&gt;&lt;/video&gt;")
///     .Build();
///
/// var html = await richText.ToHtmlAsync(resolver);
/// </code>
/// </example>
public sealed class HtmlResolverBuilder : IHtmlResolverBuilder
{
    private readonly List<ConditionalHtmlNodeResolver> _conditionalHtmlNodeResolvers = [];
    private readonly Dictionary<string, Func<IEmbeddedContent, ValueTask<string>>> _embeddedContentResolvers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, Func<IEmbeddedContent, ValueTask<string>>> _typeBasedContentResolvers = [];
    private readonly Dictionary<string, BlockResolver<IContentItemLink>> _contentItemLinkResolvers = new(StringComparer.OrdinalIgnoreCase);
    private BlockResolver<IContentItemLink>? _contentItemLinkResolver;
    private BlockResolver<IInlineImage>? _inlineImageResolver;
    private BlockResolver<ITextNode>? _textNodeResolver;
    private BlockResolver<IHtmlNode>? _htmlElementResolver;
    private bool _throwOnMissingResolver;

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentItemLinkResolver(BlockResolver<IContentItemLink> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _contentItemLinkResolver = resolver;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentItemLinkResolver(
        string contentTypeCodename,
        BlockResolver<IContentItemLink> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeCodename);
        ArgumentNullException.ThrowIfNull(resolver);

        _contentItemLinkResolvers[contentTypeCodename] = resolver;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentItemLinkResolvers(
        IReadOnlyDictionary<string, BlockResolver<IContentItemLink>> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        foreach (var (codename, resolver) in resolvers)
        {
            WithContentItemLinkResolver(codename, resolver);
        }
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentItemLinkResolvers(
        params (string ContentTypeCodename, BlockResolver<IContentItemLink> Resolver)[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        foreach (var (codename, resolver) in resolvers)
        {
            WithContentItemLinkResolver(codename, resolver);
        }
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolver(
        string contentTypeCodename,
        Func<IEmbeddedContent, ValueTask<string>> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeCodename);
        ArgumentNullException.ThrowIfNull(resolver);

        _embeddedContentResolvers[contentTypeCodename] = resolver;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolver(
        string contentTypeCodename,
        Func<IEmbeddedContent, string> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeCodename);
        ArgumentNullException.ThrowIfNull(resolver);

        // Wrap synchronous resolver in ValueTask
        _embeddedContentResolvers[contentTypeCodename] = content =>
            ValueTask.FromResult(resolver(content));
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolver<TModel>(
        Func<IEmbeddedContent<TModel>, ValueTask<string>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        // Wrap the generic resolver to work with the base interface
        _typeBasedContentResolvers[typeof(TModel)] = content =>
            content is IEmbeddedContent<TModel> typed
                ? resolver(typed)
                : ValueTask.FromResult(string.Empty);

        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolver<TModel>(
        Func<IEmbeddedContent<TModel>, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        // Wrap synchronous generic resolver
        _typeBasedContentResolvers[typeof(TModel)] = content =>
            content is IEmbeddedContent<TModel> typed
                ? ValueTask.FromResult(resolver(typed))
                : ValueTask.FromResult(string.Empty);

        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolvers(
        IReadOnlyDictionary<string, Func<IEmbeddedContent, string>> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        foreach (var (codename, resolver) in resolvers)
        {
            WithContentResolver(codename, resolver);
        }
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolvers(
        params (string ContentTypeCodename, Func<IEmbeddedContent, string> Resolver)[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        foreach (var (codename, resolver) in resolvers)
        {
            WithContentResolver(codename, resolver);
        }
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolvers(
        IReadOnlyDictionary<Type, Func<IEmbeddedContent, string>> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        foreach (var (type, resolver) in resolvers)
        {
            // Wrap synchronous resolver in ValueTask
            _typeBasedContentResolvers[type] = content =>
                ValueTask.FromResult(resolver(content));
        }

        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithContentResolvers(
        params (Type ModelType, Func<IEmbeddedContent, string> Resolver)[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        foreach (var (type, resolver) in resolvers)
        {
            // Wrap synchronous resolver in ValueTask
            _typeBasedContentResolvers[type] = content =>
                ValueTask.FromResult(resolver(content));
        }

        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithInlineImageResolver(BlockResolver<IInlineImage> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _inlineImageResolver = resolver;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithTextNodeResolver(BlockResolver<ITextNode> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _textNodeResolver = resolver;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithHtmlNodeResolver(
        HtmlNodePredicate predicate,
        BlockResolver<IHtmlNode> resolver,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(resolver);

        _conditionalHtmlNodeResolvers.Add(new ConditionalHtmlNodeResolver(
            predicate,
            resolver,
            description));

        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithHtmlNodeResolver(
        string tagName,
        BlockResolver<IHtmlNode> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        ArgumentNullException.ThrowIfNull(resolver);

        _conditionalHtmlNodeResolvers.Add(new ConditionalHtmlNodeResolver(
            node => node.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase),
            resolver,
            Description: $"Tag={tagName}",
            TagName: tagName));

        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithHtmlNodeResolverForAttribute(
        string attributeName,
        string? attributeValue,
        BlockResolver<IHtmlNode> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        ArgumentNullException.ThrowIfNull(resolver);

        HtmlNodePredicate predicate = attributeValue is null
            ? node => node.Attributes.ContainsKey(attributeName)
            : node => node.Attributes.TryGetValue(attributeName, out var value)
                   && value?.Equals(attributeValue, StringComparison.OrdinalIgnoreCase) == true;

        var description = attributeValue is null
            ? $"Attribute={attributeName}"
            : $"Attribute={attributeName}[{attributeValue}]";

        return WithHtmlNodeResolver(predicate, resolver, description);
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder WithHtmlElementResolver(BlockResolver<IHtmlNode> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _htmlElementResolver = resolver;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolverBuilder ThrowOnMissingResolver(bool enabled = true)
    {
        _throwOnMissingResolver = enabled;
        return this;
    }

    /// <inheritdoc />
    public IHtmlResolver Build() => new HtmlResolver(new HtmlResolverOptions
    {
        // Text nodes, inline images and HTML elements all render sensibly without configuration, so
        // these three are never absent. A content item link has no default: its URL is the
        // application's to decide, and an unresolved one reports itself rather than guessing.
        TextNodeResolver = _textNodeResolver ?? DefaultTextNodeResolver,
        InlineImageResolver = _inlineImageResolver ?? DefaultInlineImageResolver,
        DefaultHtmlNodeResolver = _htmlElementResolver ?? DefaultResolvers.HtmlElementResolver(),
        ContentItemLinkResolver = _contentItemLinkResolver,

        ConditionalHtmlNodeResolvers = [.. _conditionalHtmlNodeResolvers],
        ThrowOnMissingResolver = _throwOnMissingResolver,
        EmbeddedContentResolvers = _embeddedContentResolvers,
        TypeBasedContentResolvers = _typeBasedContentResolvers,
        ContentItemLinkResolvers = _contentItemLinkResolvers
    });

    private static readonly BlockResolver<ITextNode> DefaultTextNodeResolver =
        (block, _) => ValueTask.FromResult(DefaultResolvers.Encoder.Encode(block.Text));

    private static readonly BlockResolver<IInlineImage> DefaultInlineImageResolver =
        (block, _) =>
        {
            var url = DefaultResolvers.Encoder.Encode(block.Url ?? string.Empty);
            var description = DefaultResolvers.Encoder.Encode(block.Description ?? string.Empty);
            return ValueTask.FromResult($"<figure><img src=\"{url}\" alt=\"{description}\" data-asset-id=\"{block.ImageId}\" /></figure>");
        };
}
