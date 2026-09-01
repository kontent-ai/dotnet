// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.Options;

namespace Kontent.Ai.Common;

/// <summary>
/// Supplies the effective options to the pieces that need them at request time.
/// </summary>
/// <remarks>
/// Handlers read through this rather than <see cref="IOptionsMonitor{TOptions}"/> directly so the client
/// name is bound once, where the handler is registered, and a test can hand a handler a fixed instance.
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
