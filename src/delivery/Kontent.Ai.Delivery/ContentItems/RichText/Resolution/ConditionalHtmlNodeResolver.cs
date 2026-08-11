namespace Kontent.Ai.Delivery.ContentItems.RichText.Resolution;

/// <summary>
/// Internal record representing a conditional resolver for HTML nodes.
/// </summary>
/// <param name="Predicate">Decides whether this resolver handles a node.</param>
/// <param name="Resolver">Renders the node.</param>
/// <param name="Description">Free-text label, for diagnostics only.</param>
/// <param name="TagName">
/// Set when the resolver was registered for a specific tag, so matching can compare the name instead of
/// invoking <paramref name="Predicate"/>. Carried as its own field rather than encoded into
/// <paramref name="Description"/>: a description is caller-supplied text, and reading dispatch out of it
/// meant any resolver whose description happened to start with the magic prefix was treated as a tag one.
/// </param>
internal sealed record ConditionalHtmlNodeResolver(
    HtmlNodePredicate Predicate,
    BlockResolver<IHtmlNode> Resolver,
    string? Description = null,
    string? TagName = null);
