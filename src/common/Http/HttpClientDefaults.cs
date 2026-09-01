// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Common.Http;

/// <summary>
/// Configuration every Kontent.ai SDK applies to the <see cref="HttpClient"/> registrations it owns.
/// </summary>
internal static class HttpClientDefaults
{
    /// <summary>
    /// Gives the client's connections a bounded lifetime so DNS changes are picked up.
    /// </summary>
    /// <remarks>
    /// The client is a keyed singleton and resolves its <see cref="HttpClient"/> once, so the handler
    /// chain it holds is never rotated - <see cref="IHttpClientFactory"/> only hands a fresh chain to a
    /// *new* <c>CreateClient</c> call. Without this, a long-running application keeps talking to whatever
    /// address it resolved at startup, indefinitely. Two minutes matches the factory's own default
    /// handler lifetime, so a connection lives no longer than it would on the non-singleton path.
    /// </remarks>
    internal static void ConfigureConnectionRecycling(IHttpClientBuilder httpClientBuilder) =>
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });
}
