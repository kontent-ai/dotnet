using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// Supplies the effective <see cref="SyncOptions"/> to the pieces that need them at request time.
/// </summary>
/// <remarks>
/// The two registration paths resolve options differently - the container reads a named
/// <see cref="IOptionsMonitor{TOptions}"/> so reconfiguration is picked up, while a standalone client
/// holds the instance it was built with. This is what lets the handler chain be built once and used by
/// both, rather than the standalone path needing a container purely to satisfy an options monitor.
/// </remarks>
internal interface ISyncOptionsAccessor
{
    SyncOptions Current { get; }
}

/// <summary>
/// Reads named options from an <see cref="IOptionsMonitor{TOptions}"/>, so configuration changes take
/// effect without rebuilding the client.
/// </summary>
internal sealed class MonitorBackedSyncOptionsAccessor(IOptionsMonitor<SyncOptions> monitor, string? optionsName)
    : ISyncOptionsAccessor
{
    public SyncOptions Current => optionsName is null ? monitor.CurrentValue : monitor.Get(optionsName);
}

/// <summary>
/// Holds a fixed options instance, for clients built outside a container.
/// </summary>
internal sealed class SnapshotSyncOptionsAccessor(SyncOptions options) : ISyncOptionsAccessor
{
    public SyncOptions Current { get; } = options;
}
