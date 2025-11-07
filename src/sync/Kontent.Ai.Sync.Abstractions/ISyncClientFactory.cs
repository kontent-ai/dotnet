namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Factory for creating and retrieving named <see cref="ISyncClient"/> instances.
/// </summary>
public interface ISyncClientFactory
{
    /// <summary>
    /// Gets a named <see cref="ISyncClient"/> instance.
    /// </summary>
    /// <param name="name">The name of the client to retrieve. This should match the name used during registration.</param>
    /// <returns>The named sync client instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a client with the specified name is not registered.</exception>
    ISyncClient Get(string name);
}
