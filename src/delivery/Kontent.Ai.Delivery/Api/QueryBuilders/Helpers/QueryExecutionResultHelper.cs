using System.Net;

namespace Kontent.Ai.Delivery.Api.QueryBuilders.Helpers;

internal static class QueryExecutionResultHelper
{
    public static IDeliveryResult<T> CreateMissingApiResultFailure<T>(string queryType, string queryTarget)
        where T : class
    {
        var error = new Error
        {
            Message = $"No API response was recorded for '{queryType}' query '{queryTarget}' after a cache miss. " +
                "A custom IDeliveryCacheManager must invoke the factory on a miss, and set FromFactory only on the value that call produced."
        };

        // Status 0 is how the SDK reports a request that got no HTTP response; none was recorded here either.
        return DeliveryResult.Failure<T>(string.Empty, default(HttpStatusCode), error);
    }

    public static IDeliveryResult<T> EnsureApiResult<T>(
        IDeliveryResult<T>? apiResult,
        string queryType,
        string queryTarget)
        where T : class
        => apiResult ?? CreateMissingApiResultFailure<T>(queryType, queryTarget);
}
