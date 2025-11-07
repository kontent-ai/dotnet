using Kontent.Ai.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Sync;

/// <summary>
/// Factory for creating and retrieving named <see cref="ISyncClient"/> instances.
/// </summary>
internal sealed class SyncClientFactory(IServiceProvider serviceProvider) : ISyncClientFactory
{
    /// <inheritdoc />
    public ISyncClient Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        try
        {
            return serviceProvider.GetRequiredKeyedService<ISyncClient>(name);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"No sync client registered with name '{name}'. Ensure you've registered the client using AddSyncClient(\"{name}\", ...).",
                ex);
        }
    }
}
