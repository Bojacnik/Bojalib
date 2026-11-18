using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="Deque{T}"/> instances; also enables collection expressions (<c>Deque&lt;int&gt; d = [1, 2, 3];</c>).</summary>
public static class Deque
{
    /// <summary>Creates a deque holding <paramref name="items"/>, front first.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The deque.</returns>
    public static Deque<T> Create<T>(ReadOnlySpan<T> items)
    {
        var deque = new Deque<T>(items.Length);
        foreach (T item in items)
        {
            deque.PushBack(item);
        }

        return deque;
    }
}

/// <summary>
/// Double-ended queue: add and remove at both ends in amortized O(1), plus O(1) indexed access. A growable circular
/// array — what <see cref="Queue{T}"/> is, but usable from both ends (.NET has no built-in deque).
/// </summary>
/// <remarks>
/// <see cref="GetSpans"/> exposes the contents as at most two spans for zero-copy bulk processing. Enumeration and
/// indexing go from front to back.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[CollectionBuilder(typeof(Deque), nameof(Deque.Create))]
public sealed class Deque<T> : IReadOnlyList<T>
{
    private RingStorage<T> _ring;

    /// <summary>Creates an empty deque.</summary>
    /// <param name="capacity">Initial capacity; it grows as needed.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public Deque(int capacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _ring = new RingStorage<T>(capacity);
    }

    /// <summary>Creates a deque holding <paramref name="items"/>, front first.</summary>
    /// <param name="items">The items.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public Deque(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        T[] array = [.. items];
        _ring = new RingStorage<T>(0) { Items = array, Count = array.Length };
    }

    /// <summary>Gets the number of elements.</summary>
    public int Count => _ring.Count;

    /// <summary>Gets the number of elements the deque can hold before it grows.</summary>
    public int Capacity => _ring.Capacity;

    /// <summary>Gets whether the deque is empty.</summary>
    public bool IsEmpty => _ring.Count == 0;

    /// <summary>Gets or sets the element at <paramref name="index"/>, where 0 is the front.</summary>
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

    /// <summary>Adds <paramref name="item"/> at the back.</summary>
    /// <param name="item">The item.</param>
    public void PushBack(T item)
    {
        GrowIfFull();
        _ring.PushBack(item);
    }

    /// <summary>Adds <paramref name="item"/> at the front.</summary>
    /// <param name="item">The item.</param>
    public void PushFront(T item)
    {
        GrowIfFull();
        _ring.PushFront(item);
    }

    /// <summary>Removes and returns the front element.</summary>
    /// <returns>The front element.</returns>
    /// <exception cref="InvalidOperationException">The deque is empty.</exception>
    public T PopFront() => _ring.TryPopFront(out T item) ? item : ThrowEmpty();

    /// <summary>Removes and returns the back element.</summary>
    /// <returns>The back element.</returns>
    /// <exception cref="InvalidOperationException">The deque is empty.</exception>
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
    /// <exception cref="InvalidOperationException">The deque is empty.</exception>
    public T PeekFront() => _ring.TryPeekFront(out T item) ? item : ThrowEmpty();

    /// <summary>Returns the back element without removing it.</summary>
    /// <returns>The back element.</returns>
    /// <exception cref="InvalidOperationException">The deque is empty.</exception>
    public T PeekBack() => _ring.TryPeekBack(out T item) ? item : ThrowEmpty();

    /// <summary>Gets the front element without removing it, if any.</summary>
    /// <param name="item">The front element.</param>
    /// <returns><see langword="true"/> if the deque has an element.</returns>
    public bool TryPeekFront([MaybeNullWhen(false)] out T item) => _ring.TryPeekFront(out item);

    /// <summary>Gets the back element without removing it, if any.</summary>
    /// <param name="item">The back element.</param>
    /// <returns><see langword="true"/> if the deque has an element.</returns>
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

    /// <summary>Ensures room for at least <paramref name="capacity"/> elements without growing.</summary>
    /// <param name="capacity">The minimum capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public void EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity > _ring.Capacity)
        {
            _ring.Resize(Math.Max(capacity, _ring.Capacity * 2));
        }
    }

    /// <summary>Removes all elements (the capacity is kept).</summary>
    public void Clear() => _ring.Clear();

    /// <summary>Enumerates front to back.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void GrowIfFull()
    {
        if (_ring.Count == _ring.Capacity)
        {
            _ring.Resize(Math.Max(4, _ring.Capacity * 2));
        }
    }

    [DoesNotReturn]
    private static T ThrowEmpty()
    {
        ThrowHelper.ThrowEmptyCollection("deque");
        return default;
    }

    /// <summary>Enumerates a <see cref="Deque{T}"/> front to back.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Deque<T> _deque;
        private readonly int _version;
        private int _index;

        internal Enumerator(Deque<T> deque)
        {
            _deque = deque;
            _version = deque._ring.Version;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly T Current => _deque._ring.Items[_deque._ring.Physical(_index)];

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _deque._ring.Version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            return ++_index < _deque._ring.Count;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
