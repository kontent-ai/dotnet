namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// One page of a multi-request enumeration: the items the page carried, and the token that fetches the next one.
/// </summary>
/// <typeparam name="T">The type of the enumerated items.</typeparam>
public sealed class DeliveryPage<T>
{
    /// <summary>
    /// This page's items.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// An opaque token that can be supplied to <see cref="DeliveryEnumeration{T}.AsPages"/> to resume enumeration after
    /// this page; <c>null</c> when this was the last page. Callers must not inspect or modify its value.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
