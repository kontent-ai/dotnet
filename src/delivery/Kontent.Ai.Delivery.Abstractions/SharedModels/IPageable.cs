namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Represents a response that carries offset pagination metadata — skip, limit, counts and the next-page URL.
/// </summary>
/// <remarks>
/// Distinct from <see cref="DeliveryEnumeration{T}"/>, which is a continuation-token walk over many requests. These
/// are different pagination models: this one is a marker on a single response, that one is the walk itself.
/// </remarks>
public interface IPageable
{
    /// <summary>
    /// Gets paging information.
    /// </summary>
    IPagination Pagination { get; }
}
