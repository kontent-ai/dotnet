namespace Kontent.Ai.Delivery.Api.Filtering;

/// <summary>
/// Renders serialized filter pairs into the query-string fragment sent to the Delivery API.
/// </summary>
/// <remarks>
/// <para>
/// Escaping is <see cref="Uri.EscapeDataString(string)"/> on both key and value, and nothing else.
/// The DSL takes raw values; encoding is applied here, once, at the boundary — so a value that is
/// already percent-encoded gets escaped a second time.
/// </para>
/// <para>
/// Pairs are rendered in the order the caller declared them. Repeated keys stay repeated and stay
/// where they were written, which is what the API's AND semantics describe.
/// </para>
/// </remarks>
internal static class FilterQueryString
{
    /// <summary>
    /// Renders <paramref name="filters"/> as <c>key=value&amp;key=value</c>, or <c>null</c> when there
    /// is nothing to send.
    /// </summary>
    internal static string? Render(IReadOnlyList<KeyValuePair<string, string>> filters)
    {
        if (filters is not { Count: > 0 })
        {
            return null;
        }

        return string.Join('&', filters.Select(static filter =>
            $"{Uri.EscapeDataString(filter.Key)}={Uri.EscapeDataString(filter.Value)}"));
    }
}
