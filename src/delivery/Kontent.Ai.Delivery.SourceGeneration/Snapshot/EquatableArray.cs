using System.Collections;
using System.Collections.Immutable;

namespace Kontent.Ai.Delivery.SourceGeneration.Snapshot;

/// <summary>
/// An immutable array compared by its contents. The incremental pipeline skips a step whose input equals the
/// previous run's, and <see cref="ImmutableArray{T}"/> compares by reference, so every list a pipeline value
/// carries has to be one of these or the step reruns on every keystroke.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
            {
                hash = hash * 31 + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> => new([.. items]);
}
