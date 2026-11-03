using System.Runtime.CompilerServices;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <summary>
/// Builds the walk every paged query returns: <see cref="DeliveryEnumeration{T}"/>'s shared page loop wrapped in this
/// SDK's pagination logging. Queries supply only how a walk fetches its pages and how to project one.
/// </summary>
/// <remarks>
/// Lives here rather than in <c>Kontent.Ai.Delivery.Abstractions</c>, which has no logging dependency.
/// </remarks>
internal static class LoggedDeliveryEnumeration
{
    // startWalk runs once per walk, as it starts, and the fetcher it returns serves every page of that walk. A query
    // fixes its request there, so a builder changed mid-walk cannot pair a continuation token with a different query.
    public static DeliveryEnumeration<TItem> Create<TItem, TResponse>(
        string queryType,
        ILogger? logger,
        Func<Func<string?, CancellationToken, Task<IDeliveryResult<TResponse>>>> startWalk,
        Func<TResponse, DeliveryPage<TItem>> selectPage,
        CancellationToken requestCancellationToken) =>
        new(
            (continuationToken, cancellationToken) =>
                WalkLoggedAsync(queryType, logger, continuationToken, startWalk, selectPage, cancellationToken),
            requestCancellationToken);

    private static async IAsyncEnumerable<DeliveryPage<TItem>> WalkLoggedAsync<TItem, TResponse>(
        string queryType,
        ILogger? logger,
        string? continuationToken,
        Func<Func<string?, CancellationToken, Task<IDeliveryResult<TResponse>>>> startWalk,
        Func<TResponse, DeliveryPage<TItem>> selectPage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (logger is not null)
        {
            LoggerMessages.PaginationStarted(logger, queryType);
        }

        var pageCount = 0;
        var totalItems = 0;

        var pages = DeliveryEnumeration<TItem>.Walk(continuationToken, startWalk(), selectPage, cancellationToken);

        await foreach (var page in pages.ConfigureAwait(false))
        {
            pageCount++;
            totalItems += page.Items.Count;
            yield return page;
        }

        // Not reached when a page fails: the walk throws, and the caller's catch is where that gets reported.
        if (logger is not null)
        {
            LoggerMessages.PaginationCompleted(logger, queryType, pageCount, totalItems);
        }
    }
}
