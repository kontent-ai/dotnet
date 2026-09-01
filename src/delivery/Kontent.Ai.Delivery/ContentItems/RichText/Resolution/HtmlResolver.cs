using System.Collections.Frozen;
using System.Text;

namespace Kontent.Ai.Delivery.ContentItems.RichText.Resolution;

/// <inheritdoc cref="IHtmlResolver" />
internal sealed class HtmlResolver : IHtmlResolver
{
    private readonly IReadOnlyDictionary<Type, Delegate> _resolvers;
    private readonly HtmlResolverOptions _options;

    // Performance cache: maps tag names to their dedicated resolvers for O(1) lookup

    // Codename-based resolvers for embedded content (components/linked items)
    private readonly FrozenDictionary<string, Func<IEmbeddedContent, ValueTask<string>>> _embeddedContentResolvers;

    // Type-based resolvers for strongly-typed embedded content (takes precedence over codename-based)
    private readonly FrozenDictionary<Type, Func<IEmbeddedContent, ValueTask<string>>> _typeBasedContentResolvers;

    // Content-type-specific resolvers for content item links
    private readonly FrozenDictionary<string, BlockResolver<IContentItemLink>> _contentItemLinkResolvers;

    // Diagnostic messages for app-specific resolvers that require configuration
    private const string MissingEmbeddedContentResolver = "<!-- [Kontent.ai SDK] Missing resolver for embedded content of type \"{0}\" (item: {1}, codename: {2}) -->";
    private const string MissingContentItemLinkResolver = "<!-- [Kontent.ai SDK] Missing resolver for link to a content type: \"{0}\" (item ID: {1}) -->";

    public HtmlResolver(
        IReadOnlyDictionary<Type, Delegate> resolvers,
        HtmlResolverOptions options)
    {
        _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Build immutable embedded content resolver cache (codename-based dispatch)
        _embeddedContentResolvers = options.EmbeddedContentResolvers?.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase) ?? FrozenDictionary<string, Func<IEmbeddedContent, ValueTask<string>>>.Empty;

        // Build immutable type-based content resolver cache (model type dispatch - takes precedence)
        _typeBasedContentResolvers = options.TypeBasedContentResolvers?.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value) ?? FrozenDictionary<Type, Func<IEmbeddedContent, ValueTask<string>>>.Empty;

        // Build immutable content item link resolver cache (content-type-based dispatch)
        _contentItemLinkResolvers = options.ContentItemLinkResolvers?.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase) ?? FrozenDictionary<string, BlockResolver<IContentItemLink>>.Empty;
    }

    public async ValueTask<string> ResolveAsync(IRichTextContent richText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(richText);

        var htmlBuilder = new StringBuilder();

        foreach (var block in richText)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await ResolveBlockAsync(block).ConfigureAwait(false);
            htmlBuilder.Append(resolved);
        }

        return htmlBuilder.ToString();
    }

    private ValueTask<string> ResolveBlockAsync(IRichTextBlock block)
    {
        return block switch
        {
            IHtmlNode htmlNode => ResolveHtmlNodeAsync(htmlNode),

            ITextNode textNode when _resolvers.TryGetValue(typeof(ITextNode), out var resolver)
                => ((BlockResolver<ITextNode>)resolver)(textNode, _ => ValueTask.FromResult(string.Empty)),

            IInlineImage image when _resolvers.TryGetValue(typeof(IInlineImage), out var resolver)
                => ((BlockResolver<IInlineImage>)resolver)(image, _ => ValueTask.FromResult(string.Empty)),

            // Content item link resolution - type-specific takes precedence
            IContentItemLink link when link.Metadata?.ContentTypeCodename is not null
                && _contentItemLinkResolvers.TryGetValue(link.Metadata.ContentTypeCodename, out var typeResolver)
                => typeResolver(link, ResolveChildrenAsync),

            // Fallback to global content item link resolver
            IContentItemLink link when _resolvers.TryGetValue(typeof(IContentItemLink), out var resolver)
                => ((BlockResolver<IContentItemLink>)resolver)(link, ResolveChildrenAsync),

            // No resolver found for content item link
            IContentItemLink link => _options.ThrowOnMissingResolver
                ? throw new InvalidOperationException($"No resolver registered for IContentItemLink (type: {link.Metadata?.ContentTypeCodename ?? "unknown"}, item ID: {link.ItemId})")
                : ValueTask.FromResult(string.Format(MissingContentItemLinkResolver, link.Metadata?.ContentTypeCodename ?? "unknown", link.ItemId)),

            // Embedded content (components/linked items) - type-based dispatch takes precedence
            IEmbeddedContent content => ResolveEmbeddedContentAsync(content),

            _ => throw new InvalidOperationException($"Unknown block type: {block.GetType().Name}")
        };
    }

    private ValueTask<string> ResolveEmbeddedContentAsync(IEmbeddedContent content)
    {
        // Priority 1: Type-based resolver (for strongly-typed embedded content)
        if (TryGetEmbeddedContentModelType(content, out var modelType) &&
            _typeBasedContentResolvers.TryGetValue(modelType, out var typeResolver))
        {
            return typeResolver(content);
        }

        // Priority 2: Codename-based resolver (existing fallback)
        if (_embeddedContentResolvers.TryGetValue(content.System.Type, out var codenameResolver))
        {
            return codenameResolver(content);
        }

        // Priority 3: Missing resolver handling
        return _options.ThrowOnMissingResolver
            ? throw new InvalidOperationException($"No resolver registered for embedded content type: {content.System.Type}")
            : ValueTask.FromResult(string.Format(MissingEmbeddedContentResolver,
                content.System.Type, content.System.Id, content.System.Codename));
    }

    private static bool TryGetEmbeddedContentModelType(IEmbeddedContent content, out Type modelType)
    {
        foreach (var implementedInterface in content.GetType().GetInterfaces())
        {
            if (implementedInterface.IsGenericType &&
                implementedInterface.GetGenericTypeDefinition() == typeof(IEmbeddedContent<>))
            {
                modelType = implementedInterface.GetGenericArguments()[0];
                return true;
            }
        }

        modelType = null!;
        return false;
    }

    private async ValueTask<string> ResolveHtmlNodeAsync(IHtmlNode node)
    {
        // Step 1: conditional resolvers in registration order - first match wins, tag registrations
        // included. A tag match is a name comparison rather than a predicate call, which is what the
        // separate lookup was for; keeping them in one pass is what makes the documented order true.
        var matchingResolver = _options.ConditionalHtmlNodeResolvers.FirstOrDefault(Matches);
        if (matchingResolver is not null)
        {
            return await matchingResolver.Resolver(node, ResolveChildrenAsync).ConfigureAwait(false);
        }

        // Step 2: Use default HTML node resolver if configured
        if (_options.DefaultHtmlNodeResolver is not null)
        {
            return await _options.DefaultHtmlNodeResolver(node, ResolveChildrenAsync).ConfigureAwait(false);
        }

        // Step 3: Ultimate fallback - built-in default
        return await DefaultResolvers.HtmlElementResolver()(node, ResolveChildrenAsync).ConfigureAwait(false);

        bool Matches(ConditionalHtmlNodeResolver candidate) => candidate.TagName is { } tag
            ? node.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase)
            : candidate.Predicate(node);
    }

    private async ValueTask<string> ResolveChildrenAsync(IEnumerable<IRichTextBlock> children)
    {
        var builder = new StringBuilder();
        foreach (var child in children)
        {
            builder.Append(await ResolveBlockAsync(child).ConfigureAwait(false));
        }
        return builder.ToString();
    }
}

/// <summary>
/// Options for configuring HTML resolver behavior.
/// </summary>
internal sealed record HtmlResolverOptions
{
    /// <summary>
    /// When true, throws if embedded content or a content item link has no registered resolver.
    /// When false - the default - the block renders as an HTML comment naming what is missing.
    /// </summary>
    public bool ThrowOnMissingResolver { get; init; } = false;

    /// <summary>
    /// Ordered list of conditional HTML node resolvers.
    /// Evaluated in order - first matching predicate wins.
    /// </summary>
    public IReadOnlyList<ConditionalHtmlNodeResolver> ConditionalHtmlNodeResolvers { get; init; } = [];

    /// <summary>
    /// Fallback resolver for HTML nodes when no conditional resolver matches.
    /// </summary>
    public BlockResolver<IHtmlNode>? DefaultHtmlNodeResolver { get; init; }

    /// <summary>
    /// Codename-based resolvers for embedded content (components and linked items).
    /// Key is the content type codename, value is the resolver function.
    /// </summary>
    public IReadOnlyDictionary<string, Func<IEmbeddedContent, ValueTask<string>>>? EmbeddedContentResolvers { get; init; }

    /// <summary>
    /// Type-based resolvers for strongly-typed embedded content.
    /// Key is the model type (e.g., typeof(Article)), value is the resolver function.
    /// Takes precedence over codename-based resolvers.
    /// </summary>
    public IReadOnlyDictionary<Type, Func<IEmbeddedContent, ValueTask<string>>>? TypeBasedContentResolvers { get; init; }

    /// <summary>
    /// Content-type-specific resolvers for content item links.
    /// Key is the content type codename, value is the resolver function.
    /// </summary>
    public IReadOnlyDictionary<string, BlockResolver<IContentItemLink>>? ContentItemLinkResolvers { get; init; }
}
