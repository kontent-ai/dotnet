using Kontent.Ai.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Sync;

/// <summary>
/// Factory for creating and retrieving named <see cref="ISyncClient"/> instances.
/// </summary>
internal sealed class SyncClientFactory(IServiceProvider serviceProvider) : ISyncClientFactory
{
    /// <inheritdoc />
    public ISyncClient Get() => Get(NamedClients.Default);

    /// <inheritdoc />
    public ISyncClient Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Resolved with the nullable overload rather than catching: the client's own registration runs
        // inside resolution, and anything it throws is also an InvalidOperationException - a
        // configureHttpClient that rejected its input used to come back relabelled as a missing
        // registration, pointing at the wrong thing entirely.
        return serviceProvider.GetKeyedService<ISyncClient>(name)
            ?? throw new InvalidOperationException(
                $"No sync client registered with name '{name}'. Ensure you've registered the client using AddSyncClient(\"{name}\", ...).");
    }
}
