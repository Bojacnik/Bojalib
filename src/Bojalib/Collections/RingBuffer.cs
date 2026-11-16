using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="RingBuffer{T}"/> instances; also enables collection expressions.</summary>
public static class RingBuffer
{
    /// <summary>Creates a ring buffer holding <paramref name="items"/>, with capacity for exactly that many (rounded up to a power of two).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The initial items, front first.</param>
    /// <returns>The ring buffer.</returns>
    public static RingBuffer<T> Create<T>(ReadOnlySpan<T> items)
    {
        var buffer = new RingBuffer<T>(Math.Max(1, items.Length));
        foreach (T item in items)
        {
            buffer.PushBack(item);
        }

        return buffer;
    }
}

/// <summary>
/// Fixed-capacity double-ended queue over a circular array. Every operation is O(1) and allocation-free. The
/// capacity is a power of two so wrapping is a bit mask instead of a division.
/// </summary>
/// <remarks>
/// When full, <see cref="PushBack"/> / <see cref="PushFront"/> either overwrite the element at the other end
/// (<c>overwrite: true</c> — a sliding window of the most recent items) or throw. <see cref="GetSpans"/> exposes the
/// contents as at most two spans for zero-copy bulk processing.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}, Capacity = {Capacity}")]
[CollectionBuilder(typeof(RingBuffer), nameof(RingBuffer.Create))]
public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private readonly int _mask;
    private int _head; // index of the front element
    private int _count;
    private int _version;

    /// <summary>Creates an empty ring buffer.</summary>
    /// <param name="capacity">The minimum capacity; rounded up to a power of two.</param>
    /// <param name="overwrite">
    /// <see langword="true"/> to overwrite the oldest element (at the opposite end) when pushing into a full buffer;
    /// <see langword="false"/> to throw instead.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1 or greater than 2^30.</exception>
    public RingBuffer(int capacity, bool overwrite = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 1 << 30);
        _items = new T[BitOperations.RoundUpToPowerOf2((uint)capacity)];
        _mask = _items.Length - 1;
        Overwrite = overwrite;
    }

    /// <summary>Gets the capacity (a power of two).</summary>
    public int Capacity => _items.Length;

    /// <summary>Gets the number of elements.</summary>
    public int Count => _count;

    /// <summary>Gets whether the buffer is empty.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Gets whether the buffer is full.</summary>
    public bool IsFull => _count == _items.Length;

    /// <summary>Gets whether pushing into a full buffer overwrites instead of throwing.</summary>
    public bool Overwrite { get; }

    /// <summary>Gets or sets the element at <paramref name="index"/>, where 0 is the front.</summary>
    /// <param name="index">The position from the front.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            return _items[(_head + index) & _mask];
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            _items[(_head + index) & _mask] = value;
            _version++;
        }
    }

    /// <summary>Adds <paramref name="item"/> at the back.</summary>
    /// <param name="item">The item.</param>
    /// <exception cref="InvalidOperationException">The buffer is full and <see cref="Overwrite"/> is off.</exception>
    public void PushBack(T item)
    {
        if (_count == _items.Length)
        {
            ThrowIfNoOverwrite();
            _items[_head] = item; // The back slot is the front slot: overwrite the oldest and advance.
            _head = (_head + 1) & _mask;
        }
        else
        {
            _items[(_head + _count) & _mask] = item;
            _count++;
        }

        _version++;
    }

    /// <summary>Adds <paramref name="item"/> at the front.</summary>
    /// <param name="item">The item.</param>
    /// <exception cref="InvalidOperationException">The buffer is full and <see cref="Overwrite"/> is off.</exception>
    public void PushFront(T item)
    {
        if (_count == _items.Length)
        {
            ThrowIfNoOverwrite();
        }
        else
        {
            _count++;
        }

        // When full this drops the back element, whose slot becomes the new front.
        _head = (_head - 1) & _mask;
        _items[_head] = item;
        _version++;
    }

    /// <summary>Adds <paramref name="item"/> at the back unless the buffer is full (regardless of <see cref="Overwrite"/>).</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if added.</returns>
    public bool TryPushBack(T item)
    {
        if (_count == _items.Length)
        {
            return false;
        }

        _items[(_head + _count) & _mask] = item;
        _count++;
        _version++;
        return true;
    }

    /// <summary>Removes and returns the front element.</summary>
    /// <returns>The front element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PopFront()
    {
        if (!TryPopFront(out T? item))
        {
            ThrowHelper.ThrowEmptyCollection("ring buffer");
        }

        return item;
    }

    /// <summary>Removes and returns the back element.</summary>
    /// <returns>The back element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PopBack()
    {
        if (!TryPopBack(out T? item))
        {
            ThrowHelper.ThrowEmptyCollection("ring buffer");
        }

        return item;
    }

    /// <summary>Removes the front element, if any.</summary>
    /// <param name="item">The removed element.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryPopFront([MaybeNullWhen(false)] out T item)
    {
        if (_count == 0)
        {
            item = default;
            return false;
        }

        item = _items[_head];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[_head] = default!;
        }

        _head = (_head + 1) & _mask;
        _count--;
        _version++;
        return true;
    }

    /// <summary>Removes the back element, if any.</summary>
    /// <param name="item">The removed element.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryPopBack([MaybeNullWhen(false)] out T item)
    {
        if (_count == 0)
        {
            item = default;
            return false;
        }

        int index = (_head + _count - 1) & _mask;
        item = _items[index];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[index] = default!;
        }

        _count--;
        _version++;
        return true;
    }

    /// <summary>Returns the front element without removing it.</summary>
    /// <returns>The front element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PeekFront()
    {
        if (_count == 0)
        {
            ThrowHelper.ThrowEmptyCollection("ring buffer");
        }

        return _items[_head];
    }

    /// <summary>Returns the back element without removing it.</summary>
    /// <returns>The back element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PeekBack()
    {
        if (_count == 0)
        {
            ThrowHelper.ThrowEmptyCollection("ring buffer");
        }

        return _items[(_head + _count - 1) & _mask];
    }

    /// <summary>
    /// Gets the contents, front to back, as at most two spans (the second is empty unless the data wraps around).
    /// No copying. The spans are invalidated by the next modification.
    /// </summary>
    /// <param name="first">The front part.</param>
    /// <param name="second">The wrapped-around rest.</param>
    public void GetSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second)
    {
        int firstLength = Math.Min(_count, _items.Length - _head);
        first = _items.AsSpan(_head, firstLength);
        second = _items.AsSpan(0, _count - firstLength);
    }

    /// <summary>Copies the contents, front to back, into <paramref name="destination"/>.</summary>
    /// <param name="destination">At least <see cref="Count"/> elements long.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void CopyTo(Span<T> destination)
    {
        if (destination.Length < _count)
        {
            throw new ArgumentException("Destination is too short.", nameof(destination));
        }

        GetSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second);
        first.CopyTo(destination);
        second.CopyTo(destination[first.Length..]);
    }

    /// <summary>Removes all elements.</summary>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_items);
        }

        _head = 0;
        _count = 0;
        _version++;
    }

    /// <summary>Enumerates front to back.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void ThrowIfNoOverwrite()
    {
        if (!Overwrite)
        {
            throw new InvalidOperationException("The ring buffer is full.");
        }
    }

    /// <summary>Enumerates a <see cref="RingBuffer{T}"/> front to back.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly RingBuffer<T> _buffer;
        private readonly int _version;
        private int _index;

        internal Enumerator(RingBuffer<T> buffer)
        {
            _buffer = buffer;
            _version = buffer._version;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly T Current => _buffer._items[(_buffer._head + _index) & _buffer._mask];

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _buffer._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            return ++_index < _buffer._count;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
