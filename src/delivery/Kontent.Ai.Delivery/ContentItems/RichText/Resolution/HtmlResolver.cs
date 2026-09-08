using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;

namespace Kontent.Ai.Delivery.ContentItems.RichText.Resolution;

/// <inheritdoc cref="IHtmlResolver" />
internal sealed class HtmlResolver : IHtmlResolver
{
    private readonly HtmlResolverOptions _options;

    // Codename-based resolvers for embedded content (components/linked items)
    private readonly FrozenDictionary<string, Func<IEmbeddedContent, ValueTask<string>>> _embeddedContentResolvers;

    // Type-based resolvers for strongly-typed embedded content (takes precedence over codename-based)
    private readonly FrozenDictionary<Type, Func<IEmbeddedContent, ValueTask<string>>> _typeBasedContentResolvers;

    // Content-type-specific resolvers for content item links
    private readonly FrozenDictionary<string, BlockResolver<IContentItemLink>> _contentItemLinkResolvers;

    // Diagnostic messages for app-specific resolvers that require configuration
    private const string MissingEmbeddedContentResolver = "<!-- [Kontent.ai SDK] Missing resolver for embedded content of type \"{0}\" (item: {1}, codename: {2}) -->";
    private const string MissingContentItemLinkResolver = "<!-- [Kontent.ai SDK] Missing resolver for link to a content type: \"{0}\" (item ID: {1}) -->";

    public HtmlResolver(HtmlResolverOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _embeddedContentResolvers = options.EmbeddedContentResolvers.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase);

        _typeBasedContentResolvers = options.TypeBasedContentResolvers.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);

        _contentItemLinkResolvers = options.ContentItemLinkResolvers.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<string> ResolveAsync(IRichTextContent richText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(richText);

        return new RenderPass(this, cancellationToken).ResolveChildrenAsync(richText);
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

    /// <remarks>
    /// A block's model type never changes, but <see cref="Type.GetInterfaces"/> allocates an array on every
    /// call - once per embedded block per resolve, in the render path. Cached across resolvers: the answer
    /// depends on the block type alone, and the set of content item types an application maps is bounded.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, Type?> EmbeddedContentModelTypes = new();

    private static bool TryGetEmbeddedContentModelType(IEmbeddedContent content, out Type modelType)
    {
        modelType = EmbeddedContentModelTypes.GetOrAdd(content.GetType(), static blockType =>
            Array.Find(
                blockType.GetInterfaces(),
                candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEmbeddedContent<>))
            ?.GetGenericArguments()[0])!;

        return modelType is not null;
    }

    /// <summary>
    /// One render of one rich text: the token it runs under and the child resolver that carries it. Kept
    /// off the resolver itself, which is shared and renders concurrently, so one render's token cannot
    /// reach another's. The token is checked before every block at every depth, not only at the top
    /// level; a resolver's public child callback carries no token, which is why the pass has to.
    /// </summary>
    private sealed class RenderPass
    {
        private readonly HtmlResolver _owner;
        private readonly CancellationToken _cancellationToken;
        private readonly Func<IEnumerable<IRichTextBlock>, ValueTask<string>> _resolveChildren;

        public RenderPass(HtmlResolver owner, CancellationToken cancellationToken)
        {
            _owner = owner;
            _cancellationToken = cancellationToken;
            _resolveChildren = ResolveChildrenAsync;
        }

        // Text nodes and inline images are leaves, so their resolvers are handed a child resolver that
        // has nothing to walk.
        private static ValueTask<string> NoChildren(IEnumerable<IRichTextBlock> children) => ValueTask.FromResult(string.Empty);

        public async ValueTask<string> ResolveChildrenAsync(IEnumerable<IRichTextBlock> children)
        {
            var builder = new StringBuilder();
            foreach (var child in children)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                builder.Append(await ResolveBlockAsync(child).ConfigureAwait(false));
            }

            _cancellationToken.ThrowIfCancellationRequested();
            return builder.ToString();
        }

        private ValueTask<string> ResolveBlockAsync(IRichTextBlock block)
        {
            var options = _owner._options;

            return block switch
            {
                IHtmlNode htmlNode => ResolveHtmlNodeAsync(htmlNode),

                ITextNode textNode => options.TextNodeResolver(textNode, NoChildren),

                IInlineImage image => options.InlineImageResolver(image, NoChildren),

                // Content item link resolution - type-specific takes precedence
                IContentItemLink link when link.Metadata?.ContentTypeCodename is not null
                    && _owner._contentItemLinkResolvers.TryGetValue(link.Metadata.ContentTypeCodename, out var typeResolver)
                    => typeResolver(link, _resolveChildren),

                // Fallback to global content item link resolver
                IContentItemLink link when options.ContentItemLinkResolver is { } resolver
                    => resolver(link, _resolveChildren),

                // No resolver found for content item link
                IContentItemLink link => options.ThrowOnMissingResolver
                    ? throw new InvalidOperationException($"No resolver registered for IContentItemLink (type: {link.Metadata?.ContentTypeCodename ?? "unknown"}, item ID: {link.ItemId})")
                    : ValueTask.FromResult(string.Format(MissingContentItemLinkResolver, link.Metadata?.ContentTypeCodename ?? "unknown", link.ItemId)),

                // Embedded content (components/linked items) - type-based dispatch takes precedence
                IEmbeddedContent content => _owner.ResolveEmbeddedContentAsync(content),

                _ => throw new InvalidOperationException($"Unknown block type: {block.GetType().Name}")
            };
        }

        private async ValueTask<string> ResolveHtmlNodeAsync(IHtmlNode node)
        {
            // Conditional resolvers in registration order - first match wins, tag registrations included. A tag
            // match is a name comparison rather than a predicate call, which is what the separate lookup was for;
            // keeping them in one pass is what makes the documented order true.
            var matchingResolver = _owner._options.ConditionalHtmlNodeResolvers.FirstOrDefault(Matches);

            return matchingResolver is not null
                ? await matchingResolver.Resolver(node, _resolveChildren).ConfigureAwait(false)
                : await _owner._options.DefaultHtmlNodeResolver(node, _resolveChildren).ConfigureAwait(false);

            bool Matches(ConditionalHtmlNodeResolver candidate) => candidate.TagName is { } tag
                ? node.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase)
                : candidate.Predicate(node);
        }
    }
}

/// <summary>
/// Everything <see cref="HtmlResolver"/> needs, assembled by <see cref="HtmlResolverBuilder.Build"/>.
/// </summary>
internal sealed record HtmlResolverOptions
{
    /// <summary>
    /// When true, throws if embedded content or a content item link has no registered resolver.
    /// When false - the default - the block renders as an HTML comment naming what is missing.
    /// </summary>
    public bool ThrowOnMissingResolver { get; init; }

    /// <summary>
    /// Ordered list of conditional HTML node resolvers.
    /// Evaluated in order - first matching predicate wins.
    /// </summary>
    public IReadOnlyList<ConditionalHtmlNodeResolver> ConditionalHtmlNodeResolvers { get; init; } = [];

    /// <summary>
    /// Renders an HTML node when no conditional resolver matches.
    /// </summary>
    public required BlockResolver<IHtmlNode> DefaultHtmlNodeResolver { get; init; }

    /// <summary>
    /// Renders a text node.
    /// </summary>
    public required BlockResolver<ITextNode> TextNodeResolver { get; init; }

    /// <summary>
    /// Renders an inline image.
    /// </summary>
    public required BlockResolver<IInlineImage> InlineImageResolver { get; init; }

    /// <summary>
    /// Renders any content item link without a content-type-specific resolver. The one block resolver with
    /// no built-in default: a link's URL is the application's to decide, not the SDK's.
    /// </summary>
    public BlockResolver<IContentItemLink>? ContentItemLinkResolver { get; init; }

    /// <summary>
    /// Codename-based resolvers for embedded content (components and linked items).
    /// Key is the content type codename.
    /// </summary>
    public IReadOnlyDictionary<string, Func<IEmbeddedContent, ValueTask<string>>> EmbeddedContentResolvers { get; init; }
        = new Dictionary<string, Func<IEmbeddedContent, ValueTask<string>>>();

    /// <summary>
    /// Type-based resolvers for strongly-typed embedded content, keyed by model type.
    /// Takes precedence over codename-based resolvers.
    /// </summary>
    public IReadOnlyDictionary<Type, Func<IEmbeddedContent, ValueTask<string>>> TypeBasedContentResolvers { get; init; }
        = new Dictionary<Type, Func<IEmbeddedContent, ValueTask<string>>>();

    /// <summary>
    /// Content-type-specific resolvers for content item links, keyed by content type codename.
    /// </summary>
    public IReadOnlyDictionary<string, BlockResolver<IContentItemLink>> ContentItemLinkResolvers { get; init; }
        = new Dictionary<string, BlockResolver<IContentItemLink>>();
}
