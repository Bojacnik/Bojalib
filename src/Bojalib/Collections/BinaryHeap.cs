using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="BinaryHeap{T}"/> instances; also enables collection expressions (<c>BinaryHeap&lt;int&gt; h = [3, 1, 2];</c>).</summary>
public static class BinaryHeap
{
    /// <summary>Creates a min-heap from <paramref name="items"/> in O(n).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The initial items.</param>
    /// <returns>The heap.</returns>
    public static BinaryHeap<T> Create<T>(ReadOnlySpan<T> items) => new(items, comparer: null);
}

/// <summary>
/// A priority queue as an array-backed 4-ary heap: the smallest element (by the comparer) is always on top.
/// <see cref="Push"/> and <see cref="Pop"/> are O(log n); a 4-ary heap is shallower and more cache-friendly than a
/// binary one. With the default comparer, built-in numeric types compare with a single machine instruction.
/// </summary>
/// <remarks>For a max-heap pass a reversed comparer, e.g. <c>Comparer&lt;int&gt;.Create((a, b) =&gt; b.CompareTo(a))</c>.</remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[CollectionBuilder(typeof(BinaryHeap), nameof(BinaryHeap.Create))]
public sealed class BinaryHeap<T> : IReadOnlyCollection<T>
{
    private const int Arity = 4;
    private const int Log2Arity = 2;

    private readonly IComparer<T>? _comparer; // null = default ordering (fast path)
    private T[] _items;
    private int _count;
    private int _version;

    /// <summary>Creates an empty heap.</summary>
    /// <param name="comparer">Orders the elements (smallest on top); <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public BinaryHeap(IComparer<T>? comparer = null)
    {
        _comparer = comparer is null || ReferenceEquals(comparer, Comparer<T>.Default) ? null : comparer;
        _items = [];
    }

    /// <summary>Creates a heap from <paramref name="items"/> in O(n) (faster than pushing them one by one).</summary>
    /// <param name="items">The initial items.</param>
    /// <param name="comparer">Orders the elements (smallest on top); <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public BinaryHeap(ReadOnlySpan<T> items, IComparer<T>? comparer = null)
        : this(comparer)
    {
        _items = items.ToArray();
        _count = _items.Length;
        Heapify();
    }

    /// <summary>Creates a heap from <paramref name="items"/> in O(n).</summary>
    /// <param name="items">The initial items.</param>
    /// <param name="comparer">Orders the elements (smallest on top); <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public BinaryHeap(IEnumerable<T> items, IComparer<T>? comparer = null)
        : this(comparer)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        _count = _items.Length;
        Heapify();
    }

    /// <summary>Gets the number of elements.</summary>
    public int Count => _count;

    /// <summary>Gets whether the heap has no elements.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Gets the comparer that orders the elements.</summary>
    public IComparer<T> Comparer => _comparer ?? Comparer<T>.Default;

    /// <summary>Adds <paramref name="item"/>. O(log n).</summary>
    /// <param name="item">The item.</param>
    public void Push(T item)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, Math.Max(4, _items.Length * 2));
        }

        SiftUp(_count++, item);
        _version++;
    }

    /// <summary>Removes and returns the smallest element. O(log n).</summary>
    /// <returns>The smallest element.</returns>
    /// <exception cref="InvalidOperationException">The heap is empty.</exception>
    public T Pop()
    {
        if (!TryPop(out T? item))
        {
            ThrowHelper.ThrowEmptyCollection("heap");
        }

        return item;
    }

    /// <summary>Removes the smallest element, if there is one.</summary>
    /// <param name="item">The removed element.</param>
    /// <returns><see langword="true"/> if an element was removed.</returns>
    public bool TryPop([MaybeNullWhen(false)] out T item)
    {
        if (_count == 0)
        {
            item = default;
            return false;
        }

        item = _items[0];
        T last = _items[--_count];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[_count] = default!;
        }

        if (_count > 0)
        {
            SiftDown(0, last);
        }

        _version++;
        return true;
    }

    /// <summary>Returns the smallest element without removing it. O(1).</summary>
    /// <returns>The smallest element.</returns>
    /// <exception cref="InvalidOperationException">The heap is empty.</exception>
    public T Peek()
    {
        if (_count == 0)
        {
            ThrowHelper.ThrowEmptyCollection("heap");
        }

        return _items[0];
    }

    /// <summary>Gets the smallest element without removing it, if there is one.</summary>
    /// <param name="item">The smallest element.</param>
    /// <returns><see langword="true"/> if the heap has an element.</returns>
    public bool TryPeek([MaybeNullWhen(false)] out T item)
    {
        if (_count == 0)
        {
            item = default;
            return false;
        }

        item = _items[0];
        return true;
    }

    /// <summary>
    /// Adds <paramref name="item"/> and then removes and returns the smallest element, in one O(log n) step.
    /// Returns <paramref name="item"/> itself without touching the heap if it is the smallest.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns>The smallest element after adding.</returns>
    public T PushPop(T item)
    {
        if (_count == 0 || !Less(_items[0], item))
        {
            return item;
        }

        T top = _items[0];
        SiftDown(0, item);
        _version++;
        return top;
    }

    /// <summary>Removes all elements.</summary>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_items, 0, _count);
        }

        _count = 0;
        _version++;
    }

    /// <summary>Ensures room for at least <paramref name="capacity"/> elements without resizing.</summary>
    /// <param name="capacity">The minimum capacity.</param>
    public void EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity > _items.Length)
        {
            Array.Resize(ref _items, Math.Max(capacity, _items.Length * 2));
        }
    }

    /// <summary>Enumerates the elements in heap (array) order — not sorted.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Less(T x, T y) => _comparer is null ? default(DefaultLess<T>).Less(x, y) : _comparer.Compare(x, y) < 0;

    private void Heapify()
    {
        for (int i = (_count - 2) >> Log2Arity; i >= 0; i--)
        {
            SiftDown(i, _items[i]);
        }
    }

    // Moves a "hole" up from `index` until `item` fits, writing each element once instead of swapping.
    private void SiftUp(int index, T item)
    {
        T[] items = _items;
        while (index > 0)
        {
            int parent = (index - 1) >> Log2Arity;
            if (!Less(item, items[parent]))
            {
                break;
            }

            items[index] = items[parent];
            index = parent;
        }

        items[index] = item;
    }

    private void SiftDown(int index, T item)
    {
        T[] items = _items;
        int count = _count;
        while (true)
        {
            int first = (index << Log2Arity) + 1;
            if (first >= count)
            {
                break;
            }

            int smallest = first;
            int last = Math.Min(first + Arity, count);
            for (int child = first + 1; child < last; child++)
            {
                if (Less(items[child], items[smallest]))
                {
                    smallest = child;
                }
            }

            if (!Less(items[smallest], item))
            {
                break;
            }

            items[index] = items[smallest];
            index = smallest;
        }

        items[index] = item;
    }

    /// <summary>Enumerates a <see cref="BinaryHeap{T}"/> in array order.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly BinaryHeap<T> _heap;
        private readonly int _version;
        private int _index;

        internal Enumerator(BinaryHeap<T> heap)
        {
            _heap = heap;
            _version = heap._version;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly T Current => _heap._items[_index];

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _heap._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            return ++_index < _heap._count;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
