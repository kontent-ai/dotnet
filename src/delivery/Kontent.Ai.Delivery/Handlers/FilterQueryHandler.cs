namespace Kontent.Ai.Delivery.Handlers;

/// <summary>
/// Appends the filter query rendered by <see cref="Api.Filtering.FilterQueryString"/> to the request URI.
/// </summary>
/// <remarks>
/// <para>
/// The filter DSL produces query parameters whose keys (<c>elements.title[eq]</c>) are only known at
/// runtime. Refit's source generator cannot build those inline, so they travel as a request option
/// rather than a method parameter and are applied here, at the edge of the pipeline.
/// </para>
/// <para>
/// <strong>This must stay idempotent.</strong> The resilience pipeline wraps the handler chain, so
/// every handler below it runs again on each retry attempt. Appending to whatever is currently on
/// <see cref="HttpRequestMessage.RequestUri"/> would therefore double the filters on attempt two —
/// wrong results, no exception, and only under retry. Instead the un-filtered URI is captured the
/// first time through and the final URI is recomputed from it on every pass, so repeated
/// invocations converge on the same value. <c>FilterQuery_IsIdenticalOnEveryRetryAttempt</c> pins
/// this.
/// </para>
/// </remarks>
internal sealed class FilterQueryHandler : DelegatingHandler
{
    /// <summary>
    /// Name of the request option carrying the rendered filter query. Referenced from
    /// <c>IDeliveryApi</c>'s <c>[Property]</c> parameters, which need a constant.
    /// </summary>
    internal const string FiltersOptionName = "Kontent.Ai.Delivery.Filters";

    /// <summary>Typed view of <see cref="FiltersOptionName"/> for reading it back here.</summary>
    private static readonly HttpRequestOptionsKey<string> Filters = new(FiltersOptionName);

    /// <summary>The URI before filters were applied, so a retry recomputes rather than re-appends.</summary>
    private static readonly HttpRequestOptionsKey<Uri> UnfilteredUri = new("Kontent.Ai.Delivery.UnfilteredUri");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(Filters, out var filters) && !string.IsNullOrEmpty(filters))
        {
            if (!request.Options.TryGetValue(UnfilteredUri, out var baseUri))
            {
                baseUri = request.RequestUri!;
                request.Options.Set(UnfilteredUri, baseUri);
            }

            request.RequestUri = Append(baseUri, filters);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static Uri Append(Uri uri, string filters)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');

        builder.Query = existing.Length == 0 ? filters : $"{existing}&{filters}";

        return builder.Uri;
    }
}
