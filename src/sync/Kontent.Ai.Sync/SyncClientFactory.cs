using Kontent.Ai.Common;

namespace Kontent.Ai.Sync;

/// <summary>
/// Factory for creating and retrieving named <see cref="ISyncClient"/> instances.
/// </summary>
internal sealed class SyncClientFactory(IServiceProvider serviceProvider) : ISyncClientFactory
{
    /// <inheritdoc />
    public ISyncClient Get() => Get(NamedClients.Default);

    /// <inheritdoc />
    public ISyncClient Get(string name) =>
        KeyedClients.Resolve<ISyncClient>(serviceProvider, name, "sync client", "AddSyncClient");
}
