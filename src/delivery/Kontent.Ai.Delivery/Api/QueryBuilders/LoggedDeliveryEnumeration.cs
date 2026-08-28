using System.Runtime.CompilerServices;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.QueryBuilders;

/// <summary>
/// The walk every paged query returns: <see cref="DeliveryEnumeration{T}"/>'s shared page loop wrapped in this SDK's
/// pagination logging. Queries supply only how to fetch a page and how to project it.
/// </summary>
/// <remarks>
/// The logging cannot live in <c>Kontent.Ai.Delivery.Abstractions</c> — it has no logging dependency — so it lives
/// here rather than being repeated in each query builder.
/// </remarks>
/// <typeparam name="TItem">The type of the enumerated items.</typeparam>
/// <typeparam name="TResponse">The per-page response the query returns.</typeparam>
internal sealed class LoggedDeliveryEnumeration<TItem, TResponse>(
    string queryType,
    ILogger? logger,
    Func<string?, CancellationToken, Task<IDeliveryResult<TResponse>>> fetchPage,
    Func<TResponse, DeliveryPage<TItem>> selectPage,
    CancellationToken requestCancellationToken) : DeliveryEnumeration<TItem>(requestCancellationToken)
{
    protected override async IAsyncEnumerable<DeliveryPage<TItem>> AsPagesCore(
        string? continuationToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (logger is not null)
        {
            LoggerMessages.PaginationStarted(logger, queryType);
        }

        var pageCount = 0;
        var totalItems = 0;

        var pages = WalkPagesAsync(continuationToken, fetchPage, selectPage, cancellationToken);

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
