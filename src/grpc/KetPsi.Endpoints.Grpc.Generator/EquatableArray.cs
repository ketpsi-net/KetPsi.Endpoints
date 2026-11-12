using System.Collections;
using System.Collections.Immutable;

namespace KetPsi.Endpoints.Grpc.Generator;

/// <summary>Immutable array with value equality (incremental generator caching).</summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public static EquatableArray<T> Empty => default;

    private ImmutableArray<T> Array => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public int Length => Array.Length;
    public bool IsEmpty => Array.Length == 0;
    public T this[int index] => Array[index];

    public static EquatableArray<T> From(IEnumerable<T>? items)
        => items is null ? default : new EquatableArray<T>(items.ToImmutableArray());

    public ImmutableArray<T> AsImmutableArray() => Array;

    public bool Equals(EquatableArray<T> other)
    {
        var a = Array;
        var b = other.Array;
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Array)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => Array.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Array).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Array).GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items) where T : IEquatable<T>
        => EquatableArray<T>.From(items);
}
