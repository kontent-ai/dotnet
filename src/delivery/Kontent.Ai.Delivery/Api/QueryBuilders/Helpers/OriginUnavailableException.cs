namespace Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;

/// <summary>
/// Thrown from a cached query's factory when the origin could not be reached, so a cache manager with
/// fail-safe may serve a stale copy. Carries the failed result: the factory runs once for every caller
/// waiting on the same key, and only the caller whose factory ran has captured one.
/// </summary>
#pragma warning disable S3871 // Internal on purpose: thrown and caught by the SDK's own cached-query path, never by a consumer
internal sealed class OriginUnavailableException(IDeliveryResult<object> result)
    : Exception("The Delivery API could not be reached.")
{
    public IDeliveryResult<object> Result { get; } = result;
}
#pragma warning restore S3871
