// Shared source, compiled into each SDK assembly - see src/common/README.md.

namespace Kontent.Ai.Common.Http;

/// <summary>
/// The ceiling <see cref="HttpClient.Timeout"/> gets in the SDKs whose default pipeline bounds each attempt.
/// </summary>
/// <remarks>
/// Not used by the Management SDK, which sets no per-attempt timeout and always applies its configured
/// ceiling.
/// </remarks>
internal static class HttpClientTimeouts
{
    /// <summary>
    /// A value set on the options is the ceiling, whatever the pipeline. Unset, only the SDK's own pipeline
    /// earns an unbounded call, because it is the only one known to bound each attempt; otherwise
    /// <paramref name="fallback"/> - <see cref="HttpClient"/>'s own default - stays, so a black-holed
    /// connection fails rather than hanging.
    /// </summary>
    internal static TimeSpan Resolve(TimeSpan? configured, bool defaultPipelineBoundsAttempts, TimeSpan fallback)
        => configured ?? (defaultPipelineBoundsAttempts ? Timeout.InfiniteTimeSpan : fallback);
}
