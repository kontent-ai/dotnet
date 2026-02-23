using System.Reflection;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Extensions;

/// <summary>
/// Bridges pre-built <see cref="SyncOptions"/> instances into the DI options system.
/// <para>
/// <see cref="IOptionsMonitor{T}"/> expects an <c>Action&lt;SyncOptions&gt;</c> that mutates
/// a framework-managed instance. When the user supplies a fully-constructed object (via
/// <see cref="SyncOptionsBuilder"/> or a direct instance), this helper copies every public
/// writable property into the DI-managed target so the two stay in sync.
/// </para>
/// <para>
/// Reflection is used so that new properties added to <see cref="SyncOptions"/> are
/// picked up automatically without requiring manual updates here.
/// </para>
/// </summary>
internal static class SyncOptionsCopyHelper
{
    private static readonly PropertyInfo[] WritableProperties = [.. typeof(SyncOptions)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property is { CanRead: true, CanWrite: true } && property.GetIndexParameters().Length == 0)];

    /// <summary>
    /// Copies all public writable properties from <paramref name="source"/> to <paramref name="target"/>.
    /// </summary>
    internal static void Copy(SyncOptions source, SyncOptions target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var property in WritableProperties)
        {
            property.SetValue(target, property.GetValue(source));
        }
    }
}
