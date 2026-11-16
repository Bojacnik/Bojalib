using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>
/// The circular array shared by <see cref="Deque{T}"/> and <see cref="CircularBuffer{T}"/>. Indices wrap with a
/// compare-and-subtract instead of a division, so any capacity works (not just powers of two). Kept as a mutable
/// struct field of its owner, so calls operate on the field in place.
/// </summary>
internal struct RingStorage<T>
{
    public T[] Items;
    public int Head;  // physical index of the front element
    public int Count;
    public int Version;

    public RingStorage(int capacity) => Items = capacity == 0 ? [] : new T[capacity];

    public readonly int Capacity => Items.Length;

    /// <summary>Physical index of the element <paramref name="offset"/> places after the front (offset &lt;= capacity).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Physical(int offset)
    {
        int index = Head + offset;
        return index >= Items.Length ? index - Items.Length : index;
    }

    public readonly ref T At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
        return ref Items[Physical(index)];
    }

    // Callers ensure there is room.
    public void PushBack(T item)
    {
        Items[Physical(Count)] = item;
        Count++;
        Version++;
    }

    public void PushFront(T item)
    {
        Head = Head == 0 ? Items.Length - 1 : Head - 1;
        Items[Head] = item;
        Count++;
        Version++;
    }

    // Full buffer: the back slot is the front slot, so overwrite the oldest element and advance.
    public void OverwriteBack(T item)
    {
        Items[Head] = item;
        Head = Physical(1);
        Version++;
    }

    // Full buffer: the slot before the front holds the back element, which is dropped.
    public void OverwriteFront(T item)
    {
        Head = Head == 0 ? Items.Length - 1 : Head - 1;
        Items[Head] = item;
        Version++;
    }

    public bool TryPopFront(out T item)
    {
        if (Count == 0)
        {
            item = default!;
            return false;
        }

        item = Items[Head];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Items[Head] = default!;
        }

        Head = Physical(1);
        Count--;
        Version++;
        return true;
    }

    public bool TryPopBack(out T item)
    {
        if (Count == 0)
        {
            item = default!;
            return false;
        }

        int index = Physical(Count - 1);
        item = Items[index];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Items[index] = default!;
        }

        Count--;
        Version++;
        return true;
    }

    public readonly bool TryPeekFront(out T item)
    {
        item = Count == 0 ? default! : Items[Head];
        return Count != 0;
    }

    public readonly bool TryPeekBack(out T item)
    {
        item = Count == 0 ? default! : Items[Physical(Count - 1)];
        return Count != 0;
    }

    public readonly void GetSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second)
    {
        int firstLength = Math.Min(Count, Items.Length - Head);
        first = Items.AsSpan(Head, firstLength);
        second = Items.AsSpan(0, Count - firstLength);
    }

    public readonly void CopyTo(Span<T> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException("Destination is too short.", nameof(destination));
        }

        GetSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second);
        first.CopyTo(destination);
        second.CopyTo(destination[first.Length..]);
    }

    /// <summary>Moves the contents into a new array of <paramref name="capacity"/>, front at index 0.</summary>
    public void Resize(int capacity)
    {
        var items = new T[capacity];
        CopyTo(items);
        Items = items;
        Head = 0;
        Version++;
    }

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            GetSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second);
            Array.Clear(Items, Head, first.Length);
            Array.Clear(Items, 0, second.Length);
        }

        Head = 0;
        Count = 0;
        Version++;
    }
}
