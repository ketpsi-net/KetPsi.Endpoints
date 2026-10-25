using System.Buffers;
using System.Runtime.CompilerServices;

namespace KetPsi.Endpoints.Grpc.Wire;

/// <summary>
/// Pooled growable buffer used by generated readers to build <c>T[]</c> fields with a single
/// exact-size allocation. Mutable struct: keep it in a local and do not copy it.
/// </summary>
public struct ProtoArrayBuilder<T>
{
    private T[]? _items;
    private int _count;

    /// <summary>Number of items added so far.</summary>
    public readonly int Count => _count;

    /// <summary>Ensures room for <paramref name="additional"/> more items.</summary>
    public void EnsureCapacity(int additional)
    {
        if (additional <= 0)
            return;
        var required = _count + additional;
        if (_items is null || required > _items.Length)
            Grow(required);
    }

    /// <summary>Appends an item.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        var items = _items;
        if (items is null || _count == items.Length)
        {
            Grow(_count + 1);
            items = _items!;
        }
        items[_count++] = item;
    }

    /// <summary>Returns an exact-size array and releases the pooled buffer.</summary>
    public T[] ToArray()
    {
        if (_items is null)
            return Array.Empty<T>();
        var result = _count == 0 ? Array.Empty<T>() : _items.AsSpan(0, _count).ToArray();
        ArrayPool<T>.Shared.Return(_items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _items = null;
        _count = 0;
        return result;
    }

    private void Grow(int required)
    {
        var newSize = Math.Max(required, _items is null ? 4 : _items.Length * 2);
        var next = ArrayPool<T>.Shared.Rent(newSize);
        if (_items is not null)
        {
            _items.AsSpan(0, _count).CopyTo(next);
            ArrayPool<T>.Shared.Return(_items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        _items = next;
    }
}
