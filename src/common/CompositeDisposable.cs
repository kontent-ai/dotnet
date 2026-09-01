// Shared source, compiled into each SDK assembly - see src/common/README.md.

namespace Kontent.Ai.Common;

/// <summary>
/// Disposes a fixed set of resources together, in order, once. What a client built outside a
/// container owns: the transport it drew and the provider it drew it from.
/// </summary>
internal sealed class CompositeDisposable(params IDisposable[] items) : IDisposable, IAsyncDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var item in items)
        {
            item.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var item in items)
        {
            if (item is IAsyncDisposable a)
            {
                await a.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                item.Dispose();
            }
        }
    }
}
