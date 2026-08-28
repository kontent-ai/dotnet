namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Fluent builder for retrieving content items that use the specified item.
/// </summary>
public interface IItemUsedInQuery
{
    /// <summary>
    /// Configures waiting for the newest content for this specific request.
    /// </summary>
    /// <param name="enabled">Whether to wait for loading new content.</param>
    IItemUsedInQuery WaitForLoadingNewContent(bool enabled = true);

    /// <summary>
    /// Adds filtering conditions to the query.
    /// </summary>
    /// <remarks>
    /// The returned query uses AND semantics between conditions (multiple query parameters).
    /// </remarks>
    /// <param name="build">Builder function that appends one or more filtering conditions.</param>
    /// <returns>The query builder for method chaining.</returns>
    IItemUsedInQuery Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build);

    /// <summary>
    /// Executes the query and returns the first page of parent content items using the Used In endpoint.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result wrapping the first page and the token that fetches the next one.</returns>
    Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query from a previously returned continuation token, resuming a walk rather than starting one.
    /// </summary>
    /// <param name="continuationToken">A token taken from <see cref="DeliveryPage{T}.ContinuationToken"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result wrapping the page following the one the token came from.</returns>
    Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(string continuationToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates parent content items using the Used In endpoint.
    /// </summary>
    /// <remarks>
    /// The returned value is both a stream of items and, via <see cref="DeliveryEnumeration{T}.AsPages"/>, a stream of
    /// pages. A failed request throws <see cref="DeliveryRequestException"/> — enumeration is a walk, not a single
    /// request, so it has no result to return. Use <see cref="ExecuteAsync(CancellationToken)"/> where non-throwing
    /// semantics are needed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token to stop enumeration and cancel in-flight requests.</param>
    /// <returns>An enumeration over the used-in items.</returns>
    DeliveryEnumeration<IUsedInItem> EnumerateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Fluent builder for retrieving content items that use the specified asset.
/// </summary>
public interface IAssetUsedInQuery
{
    /// <summary>
    /// Configures waiting for the newest content for this specific request.
    /// </summary>
    /// <param name="enabled">Whether to wait for loading new content.</param>
    IAssetUsedInQuery WaitForLoadingNewContent(bool enabled = true);

    /// <summary>
    /// Adds filtering conditions to the query.
    /// </summary>
    /// <remarks>
    /// The returned query uses AND semantics between conditions (multiple query parameters).
    /// </remarks>
    /// <param name="build">Builder function that appends one or more filtering conditions.</param>
    /// <returns>The query builder for method chaining.</returns>
    IAssetUsedInQuery Where(Func<IItemsFilterBuilder, IItemsFilterBuilder> build);

    /// <summary>
    /// Executes the query and returns the first page of parent content items using the Asset Used In endpoint.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result wrapping the first page and the token that fetches the next one.</returns>
    Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query from a previously returned continuation token, resuming a walk rather than starting one.
    /// </summary>
    /// <param name="continuationToken">A token taken from <see cref="DeliveryPage{T}.ContinuationToken"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result wrapping the page following the one the token came from.</returns>
    Task<IDeliveryResult<DeliveryPage<IUsedInItem>>> ExecuteAsync(string continuationToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates parent content items using the Asset Used In endpoint.
    /// </summary>
    /// <remarks>
    /// The returned value is both a stream of items and, via <see cref="DeliveryEnumeration{T}.AsPages"/>, a stream of
    /// pages. A failed request throws <see cref="DeliveryRequestException"/> — enumeration is a walk, not a single
    /// request, so it has no result to return. Use <see cref="ExecuteAsync(CancellationToken)"/> where non-throwing
    /// semantics are needed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token to stop enumeration and cancel in-flight requests.</param>
    /// <returns>An enumeration over the used-in items.</returns>
    DeliveryEnumeration<IUsedInItem> EnumerateAsync(CancellationToken cancellationToken = default);
}
