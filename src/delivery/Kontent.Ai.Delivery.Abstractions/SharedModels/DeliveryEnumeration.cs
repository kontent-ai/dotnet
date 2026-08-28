using System.Runtime.CompilerServices;

namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// A multi-request enumeration over a Delivery API collection, viewable either as a stream of items or as a stream of
/// pages.
/// </summary>
/// <remarks>
/// Enumeration is a composed operation over many requests, so it does not return an <see cref="IDeliveryResult{T}"/>:
/// a failed request throws <see cref="DeliveryRequestException"/>. Where non-throwing semantics are needed, use the
/// query's single-request <c>ExecuteAsync</c> instead — one request returns a result, a walk throws.
/// <para>
/// Derived types implement only <see cref="AsPagesCore"/>. The base derives item enumeration from the page walk, so
/// the two views cannot disagree, and it owns cancellation: the token captured at construction is combined with the
/// one supplied to <see cref="GetAsyncEnumerator"/> or <c>WithCancellation</c>.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the enumerated items.</typeparam>
public abstract class DeliveryEnumeration<T> : IAsyncEnumerable<T>
{
    private readonly CancellationToken _requestCancellationToken;

    /// <summary>
    /// Initializes the enumeration with the cancellation token of the request that created it.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token supplied by the caller when the enumeration was requested. It is combined with any token supplied later
    /// at enumeration time; either one firing cancels the walk.
    /// </param>
    protected DeliveryEnumeration(CancellationToken cancellationToken = default)
    {
        _requestCancellationToken = cancellationToken;
    }

    /// <summary>
    /// Fetches the pages. The only member a derived type implements; the base handles cancellation and item flattening.
    /// </summary>
    /// <param name="continuationToken">Token to resume from, or <c>null</c> to start at the first page.</param>
    /// <param name="cancellationToken">The effective token for the walk, already combined by the base.</param>
    protected abstract IAsyncEnumerable<DeliveryPage<T>> AsPagesCore(string? continuationToken, CancellationToken cancellationToken);

    /// <summary>
    /// Views the enumeration as a stream of pages, exposing each page's continuation token so a walk can be
    /// checkpointed and resumed. Each iteration is one HTTP request; a failed request throws.
    /// </summary>
    /// <param name="continuationToken">
    /// A token previously taken from <see cref="DeliveryPage{T}.ContinuationToken"/> to resume from, or <c>null</c> to
    /// start at the first page.
    /// </param>
    // An empty token means the same as none, matching what a finished walk hands back. None, not the captured token:
    // EnumeratePagesAsync resolves that itself, so a token arriving later through WithCancellation is combined rather
    // than ignored.
    public IAsyncEnumerable<DeliveryPage<T>> AsPages(string? continuationToken = null) =>
        EnumeratePagesAsync(string.IsNullOrEmpty(continuationToken) ? null : continuationToken, CancellationToken.None);

    /// <inheritdoc />
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await foreach (var page in AsPages().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in page.Items)
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Creates an enumeration over a fixed set of pages, without any transport. Intended for tests and fakes; the
    /// continuation token passed to <see cref="AsPages"/> is ignored.
    /// </summary>
    /// <param name="pages">The pages to yield, in order.</param>
    public static DeliveryEnumeration<T> FromPages(IEnumerable<DeliveryPage<T>> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        return new FixedEnumeration(pages);
    }

    /// <summary>
    /// Walks pages until the continuation token runs out, turning a failed request into a throw. The one place that
    /// logic lives, so no derived enumeration can drift on how a walk terminates or how a failure surfaces.
    /// </summary>
    /// <typeparam name="TResponse">The per-page response the fetch returns.</typeparam>
    /// <param name="continuationToken">Token to resume from, or <c>null</c> to start at the first page.</param>
    /// <param name="fetchPage">Fetches one page for a given token.</param>
    /// <param name="selectPage">Projects a fetched response onto the page shape.</param>
    /// <param name="cancellationToken">The effective token for the walk.</param>
    protected static IAsyncEnumerable<DeliveryPage<T>> WalkPagesAsync<TResponse>(
        string? continuationToken,
        Func<string?, CancellationToken, Task<IDeliveryResult<TResponse>>> fetchPage,
        Func<TResponse, DeliveryPage<T>> selectPage,
        CancellationToken cancellationToken)
    {
        // Guarded here rather than in the iterator, so a null argument throws at the call rather than on first pull.
        ArgumentNullException.ThrowIfNull(fetchPage);
        ArgumentNullException.ThrowIfNull(selectPage);

        return WalkPagesCoreAsync(continuationToken, fetchPage, selectPage, cancellationToken);
    }

    private static async IAsyncEnumerable<DeliveryPage<T>> WalkPagesCoreAsync<TResponse>(
        string? continuationToken,
        Func<string?, CancellationToken, Task<IDeliveryResult<TResponse>>> fetchPage,
        Func<TResponse, DeliveryPage<T>> selectPage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Local to this walk: two enumerations of one DeliveryEnumeration never share continuation state.
        var token = continuationToken;

        while (true)
        {
            var result = await fetchPage(token, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                throw new DeliveryRequestException(
                    result.Error?.Message ?? $"The Delivery API request failed with status code {(int)result.StatusCode}.",
                    result.StatusCode,
                    result.Error,
                    result.RequestUrl);
            }

            var page = selectPage(result.Value);
            yield return page;

            if (page.ContinuationToken is null)
            {
                yield break;
            }

            token = page.ContinuationToken;
        }
    }

    private async IAsyncEnumerable<DeliveryPage<T>> EnumeratePagesAsync(
        string? continuationToken,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Linking allocates a registration on both tokens, so only do it when both can actually fire and differ.
        CancellationTokenSource? linked = null;
        CancellationToken effectiveToken;

        if (!cancellationToken.CanBeCanceled)
        {
            effectiveToken = _requestCancellationToken;
        }
        else if (!_requestCancellationToken.CanBeCanceled || _requestCancellationToken == cancellationToken)
        {
            effectiveToken = cancellationToken;
        }
        else
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(_requestCancellationToken, cancellationToken);
            effectiveToken = linked.Token;
        }

        try
        {
            await foreach (var page in AsPagesCore(continuationToken, effectiveToken).ConfigureAwait(false))
            {
                yield return page;
            }
        }
        finally
        {
            // Runs when the enumerator is disposed, including when the consumer breaks out early.
            linked?.Dispose();
        }
    }

    private sealed class FixedEnumeration(IEnumerable<DeliveryPage<T>> pages) : DeliveryEnumeration<T>
    {
        protected override async IAsyncEnumerable<DeliveryPage<T>> AsPagesCore(
            string? continuationToken,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return page;
            }
        }
    }
}
