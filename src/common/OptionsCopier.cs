// Shared source, compiled into each SDK assembly - see src/common/README.md.

using System.Reflection;

namespace Kontent.Ai.Common;

/// <summary>
/// Bridges a pre-built options instance into the DI options system.
/// </summary>
/// <remarks>
/// The options pipeline configures an instance the container owns, so it takes an
/// <c>Action&lt;TOptions&gt;</c> that mutates that instance rather than a replacement for it. A caller who
/// hands over a fully-constructed object therefore needs its values copied across.
/// <para>
/// Reflection rather than a hand-written assignment per property, because a hand-written one silently
/// drops any property added later - the registration keeps compiling and quietly stops carrying the new
/// value. The property list is resolved once per type.
/// </para>
/// </remarks>
/// <typeparam name="TOptions">The options type being copied.</typeparam>
internal static class OptionsCopier<TOptions> where TOptions : class
{
    private static readonly PropertyInfo[] WritableProperties = [.. typeof(TOptions)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property is { CanRead: true, CanWrite: true } && property.GetIndexParameters().Length == 0)];

    /// <summary>
    /// Copies every public readable and writable property from <paramref name="source"/> to
    /// <paramref name="target"/>.
    /// </summary>
    internal static void Copy(TOptions source, TOptions target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var property in WritableProperties)
        {
            property.SetValue(target, property.GetValue(source));
        }
    }
}
