using System.Collections;
using System.Collections.Immutable;

namespace AgentPrism.Generators;

/// <summary>
/// A value type that wraps <see cref="ImmutableArray{T}"/> with element-based structural equality.
/// </summary>
/// <remarks>
/// <see cref="IIncrementalGenerator"/> caching depends on model types implementing
/// <see cref="object.Equals(object)"/> and <see cref="object.GetHashCode"/> correctly.
/// <see cref="ImmutableArray{T}"/> does not provide this itself because its default
/// equality is reference/default equality. Without this provider, the generator
/// regenerates everything even when an unrelated file changes.
/// </remarks>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    public static EquatableArray<T> Empty { get; } = new(ImmutableArray<T>.Empty);

    public int Count => _items.IsDefault ? 0 : _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _items.IsDefault ? ImmutableArray<T>.Empty : _items;
        var right = other._items.IsDefault ? ImmutableArray<T>.Empty : other._items;

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_items.IsDefault)
        {
            return 0;
        }

        // netstandard2.0 has no System.HashCode, so combine values manually in an FNV-like way.
        unchecked
        {
            var hash = 17;
            foreach (var item in _items)
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
        => ((IEnumerable<T>)(_items.IsDefault ? ImmutableArray<T>.Empty : _items)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items);
}
