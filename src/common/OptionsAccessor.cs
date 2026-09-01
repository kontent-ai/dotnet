// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.Options;

namespace Kontent.Ai.Common;

/// <summary>
/// Supplies the effective options to the pieces that need them at request time.
/// </summary>
/// <remarks>
/// The two registration paths resolve options differently - the container reads a named
/// <see cref="IOptionsMonitor{TOptions}"/> so reconfiguration is picked up, while a client built outside
/// one holds the instance it was given. This is what lets a handler chain be built once and used by both,
/// rather than the container-free path needing a container purely to satisfy an options monitor.
/// </remarks>
internal interface IOptionsAccessor<out TOptions>
    where TOptions : class
{
    TOptions Current { get; }
}

/// <summary>
/// Reads named options from an <see cref="IOptionsMonitor{TOptions}"/>, so configuration changes take
/// effect without rebuilding the client.
/// </summary>
/// <remarks>
/// The name is required. A null one reads the unnamed registration, which exists only where a default
/// client was registered - so a named-only setup would resolve options nobody configured and build
/// requests against a blank environment instead of failing.
/// </remarks>
internal sealed class MonitorBackedOptionsAccessor<TOptions>(IOptionsMonitor<TOptions> monitor, string optionsName)
    : IOptionsAccessor<TOptions>
    where TOptions : class
{
    public TOptions Current => monitor.Get(optionsName);
}

/// <summary>
/// Holds a fixed options instance, for clients built outside a container.
/// </summary>
internal sealed class SnapshotOptionsAccessor<TOptions>(TOptions options)
    : IOptionsAccessor<TOptions>
    where TOptions : class
{
    public TOptions Current { get; } = options;
}
