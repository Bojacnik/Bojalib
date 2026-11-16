using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="CircularBuffer{T}"/> instances; also enables collection expressions.</summary>
public static class CircularBuffer
{
    /// <summary>Creates a full buffer holding <paramref name="items"/>, front first; its capacity is the number of items.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The buffer.</returns>
    public static CircularBuffer<T> Create<T>(ReadOnlySpan<T> items)
    {
        var buffer = new CircularBuffer<T>(Math.Max(1, items.Length));
        foreach (T item in items)
        {
            buffer.PushBack(item);
        }

        return buffer;
    }
}

/// <summary>
/// Fixed-capacity circular buffer: a sliding window of the most recent items. When full, adding at one end drops the
/// element at the other end, so it never allocates after construction. O(1) push, pop and indexed access at both ends.
/// </summary>
/// <remarks>
/// Typical uses: the last N log lines, samples for a moving average, bounded undo history. The capacity is exactly
/// what you ask for. Use <see cref="TryPushBack"/> / <see cref="TryPushFront"/> to refuse instead of overwriting.
/// <see cref="GetSpans"/> exposes the contents as at most two spans for zero-copy bulk processing.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}, Capacity = {Capacity}")]
[CollectionBuilder(typeof(CircularBuffer), nameof(CircularBuffer.Create))]
public sealed class CircularBuffer<T> : IReadOnlyList<T>
{
    private RingStorage<T> _ring;

    /// <summary>Creates an empty buffer.</summary>
    /// <param name="capacity">How many elements it holds before overwriting.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public CircularBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new RingStorage<T>(capacity);
    }

    /// <summary>Gets the number of elements.</summary>
    public int Count => _ring.Count;

    /// <summary>Gets how many elements the buffer holds before it starts overwriting.</summary>
    public int Capacity => _ring.Capacity;

    /// <summary>Gets whether the buffer is empty.</summary>
    public bool IsEmpty => _ring.Count == 0;

    /// <summary>Gets whether the buffer is full, so the next push overwrites.</summary>
    public bool IsFull => _ring.Count == _ring.Capacity;

    /// <summary>Gets or sets the element at <paramref name="index"/>, where 0 is the front (oldest).</summary>
    /// <param name="index">The position from the front.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public T this[int index]
    {
        get => _ring.At(index);
        set
        {
            _ring.At(index) = value;
            _ring.Version++;
        }
    }

    /// <summary>Adds <paramref name="item"/> at the back. If full, the front (oldest) element is dropped.</summary>
    /// <param name="item">The item.</param>
    public void PushBack(T item)
    {
        if (IsFull)
        {
            _ring.OverwriteBack(item);
        }
        else
        {
            _ring.PushBack(item);
        }
    }

    /// <summary>Adds <paramref name="item"/> at the front. If full, the back element is dropped.</summary>
    /// <param name="item">The item.</param>
    public void PushFront(T item)
    {
        if (IsFull)
        {
            _ring.OverwriteFront(item);
        }
        else
        {
            _ring.PushFront(item);
        }
    }

    /// <summary>Adds <paramref name="item"/> at the back unless the buffer is full.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if added.</returns>
    public bool TryPushBack(T item)
    {
        if (IsFull)
        {
            return false;
        }

        _ring.PushBack(item);
        return true;
    }

    /// <summary>Adds <paramref name="item"/> at the front unless the buffer is full.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if added.</returns>
    public bool TryPushFront(T item)
    {
        if (IsFull)
        {
            return false;
        }

        _ring.PushFront(item);
        return true;
    }

    /// <summary>Removes and returns the front (oldest) element.</summary>
    /// <returns>The front element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PopFront() => _ring.TryPopFront(out T item) ? item : ThrowEmpty();

    /// <summary>Removes and returns the back (newest) element.</summary>
    /// <returns>The back element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PopBack() => _ring.TryPopBack(out T item) ? item : ThrowEmpty();

    /// <summary>Removes the front element, if any.</summary>
    /// <param name="item">The removed element.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryPopFront([MaybeNullWhen(false)] out T item) => _ring.TryPopFront(out item);

    /// <summary>Removes the back element, if any.</summary>
    /// <param name="item">The removed element.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryPopBack([MaybeNullWhen(false)] out T item) => _ring.TryPopBack(out item);

    /// <summary>Returns the front element without removing it.</summary>
    /// <returns>The front element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PeekFront() => _ring.TryPeekFront(out T item) ? item : ThrowEmpty();

    /// <summary>Returns the back element without removing it.</summary>
    /// <returns>The back element.</returns>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T PeekBack() => _ring.TryPeekBack(out T item) ? item : ThrowEmpty();

    /// <summary>Gets the front element without removing it, if any.</summary>
    /// <param name="item">The front element.</param>
    /// <returns><see langword="true"/> if the buffer has an element.</returns>
    public bool TryPeekFront([MaybeNullWhen(false)] out T item) => _ring.TryPeekFront(out item);

    /// <summary>Gets the back element without removing it, if any.</summary>
    /// <param name="item">The back element.</param>
    /// <returns><see langword="true"/> if the buffer has an element.</returns>
    public bool TryPeekBack([MaybeNullWhen(false)] out T item) => _ring.TryPeekBack(out item);

    /// <summary>
    /// Gets the contents, front to back, as at most two spans (the second is empty unless the data wraps around).
    /// No copying; the spans are invalidated by the next modification.
    /// </summary>
    /// <param name="first">The front part.</param>
    /// <param name="second">The wrapped-around rest.</param>
    public void GetSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second) => _ring.GetSpans(out first, out second);

    /// <summary>Copies the contents, front to back, into <paramref name="destination"/>.</summary>
    /// <param name="destination">At least <see cref="Count"/> elements long.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void CopyTo(Span<T> destination) => _ring.CopyTo(destination);

    /// <summary>Removes all elements.</summary>
    public void Clear() => _ring.Clear();

    /// <summary>Enumerates front (oldest) to back (newest).</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [DoesNotReturn]
    private static T ThrowEmpty()
    {
        ThrowHelper.ThrowEmptyCollection("circular buffer");
        return default;
    }

    /// <summary>Enumerates a <see cref="CircularBuffer{T}"/> front to back.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly CircularBuffer<T> _buffer;
        private readonly int _version;
        private int _index;

        internal Enumerator(CircularBuffer<T> buffer)
        {
            _buffer = buffer;
            _version = buffer._ring.Version;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly T Current => _buffer._ring.Items[_buffer._ring.Physical(_index)];

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _buffer._ring.Version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            return ++_index < _buffer._ring.Count;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
