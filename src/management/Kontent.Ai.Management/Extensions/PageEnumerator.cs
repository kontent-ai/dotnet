namespace Kontent.Ai.Management.Extensions;

/// <summary>
/// Drives continuation-token paging for the client's materialized listing methods: walks every page, one HTTP
/// request at a time, and merges them. Callers that need the pages themselves use the client's
/// <c>List{Plural}PageAsync</c> methods instead of this.
/// </summary>
internal static class PageEnumerator
{
    /// <summary>
    /// Drains every page into a single result. The first failed page short-circuits and is returned as the failure,
    /// so a listing is all-or-nothing rather than a partial set.
    /// </summary>
    public static async Task<IManagementResult<IReadOnlyList<TItem>>> CollectAsync<TPage, TItem>(
        Func<string?, CancellationToken, Task<IApiResponse<TPage>>> fetchPage,
        Func<TPage, IEnumerable<TItem>> selectItems,
        Func<TPage, string?> selectContinuationToken,
        CancellationToken cancellationToken = default)
    {
        var items = new List<TItem>();
        string? continuationToken = null;

        while (true)
        {
            var response = await fetchPage(continuationToken, cancellationToken).ConfigureAwait(false);

            // Read the token first: mapping the response disposes it.
            var nextToken = response.Content is null ? null : selectContinuationToken(response.Content);

            var page = await response
                .ToManagementResultAsync<TPage, IReadOnlyList<TItem>>(content => selectItems(content).ToList())
                .ConfigureAwait(false);

            if (!page.IsSuccess)
            {
                return page;
            }

            items.AddRange(page.Value);

            if (nextToken is null)
            {
                return ManagementResult<IReadOnlyList<TItem>>.Success(items, page.StatusCode, page.RequestUrl);
            }

            continuationToken = nextToken;
        }
    }
}
