// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// The default pipeline of the read-only SDKs: retry on anything transient, honouring <c>Retry-After</c>,
/// with a per-attempt timeout inside the retry so a hung attempt is retried on a fresh connection.
/// </summary>
/// <remarks>
/// Not used by the Management SDK, which writes: its retries are idempotency-aware and it sets no
/// per-attempt timeout, since an asset upload takes as long as it takes.
/// </remarks>
internal static class DefaultResilience
{
    internal static void ConfigureReadPipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                HttpRetryPredicates.IsTransientException(args.Outcome.Exception, args.Context.CancellationToken) ||
                (args.Outcome.Result?.IsSuccessStatusCode == false &&
                 HttpRetryPredicates.IsRetryableStatusCode(args.Outcome.Result?.StatusCode))),
            DelayGenerator = HttpRetryDelay.FromRetryAfterHeader
        });

        builder.AddTimeout(TimeSpan.FromSeconds(30));
    }
}
