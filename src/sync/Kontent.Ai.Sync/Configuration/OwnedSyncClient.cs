using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// A delegating wrapper that owns the <see cref="ServiceProvider"/> lifetime.
/// Created by <see cref="SyncClientBuilder"/> so that disposing the client
/// tears down the internal service provider and all registered services.
/// </summary>
internal sealed class OwnedSyncClient(ServiceProvider serviceProvider, ISyncClient inner) : ISyncClient
{
    private int _disposeState;

    public Task<ISyncResult<ISyncInitResponse>> InitializeSyncAsync(CancellationToken cancellationToken = default)
        => inner.InitializeSyncAsync(cancellationToken);

    public Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default)
        => inner.GetDeltaAsync(syncToken, cancellationToken);

    public IAsyncEnumerable<ISyncResult<ISyncDeltaResponse>> EnumerateDeltaAsync(string syncToken, CancellationToken cancellationToken = default)
        => inner.EnumerateDeltaAsync(syncToken, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        serviceProvider.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        await serviceProvider.DisposeAsync().ConfigureAwait(false);
    }
}
