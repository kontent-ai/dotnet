namespace Kontent.Ai.Management.Models.Shared;

/// <summary>
/// One page of a continuation-token listing: the items the page carried, and the token that fetches the next one.
/// </summary>
/// <remarks>
/// The token is opaque and server-issued. Passing it back resumes the walk where it stopped, so an interrupted
/// listing — a rate limit outliving the retry pipeline, say — costs one more request rather than a restart. How long
/// a token stays valid is the Management API's contract, not this SDK's.
/// </remarks>
/// <typeparam name="T">The type of the listed items.</typeparam>
public sealed class ListingPage<T>
{
    /// <summary>
    /// This page's items.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// The token that fetches the page after this one; <c>null</c> on the last page.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
