using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="BTreeSet{T}"/> instances; also enables collection expressions.</summary>
public static class BTreeSet
{
    /// <summary>Creates a set holding <paramref name="items"/> (duplicates are ignored).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The set.</returns>
    public static BTreeSet<T> Create<T>(ReadOnlySpan<T> items)
        where T : notnull
    {
        var set = new BTreeSet<T>();
        foreach (T item in items)
        {
            set.Add(item);
        }

        return set;
    }
}

/// <summary>
/// A sorted set backed by a B+tree that also knows positions: O(log n) add, remove and lookup, plus
/// <c>set[i]</c> (the i-th smallest element), <see cref="IndexOf"/>, <see cref="CountLessThan"/>, floor/ceiling and
/// range enumeration — which <see cref="SortedSet{T}"/> can't do in O(log n). See
/// <see cref="BTreeDictionary{TKey, TValue}"/> for the design.
/// </summary>
/// <remarks>Typical uses: leaderboards ("what rank is this score?"), percentiles over a changing set, ordered indexes.</remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[CollectionBuilder(typeof(BTreeSet), nameof(BTreeSet.Create))]
public sealed class BTreeSet<T> : IReadOnlyList<T>
    where T : notnull
{
    private readonly BTreeDictionary<T, NoValue> _tree;

    /// <summary>Creates an empty set.</summary>
    /// <param name="comparer">Orders the elements; <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public BTreeSet(IComparer<T>? comparer = null) => _tree = new BTreeDictionary<T, NoValue>(comparer);

    /// <summary>Gets the number of elements.</summary>
    public int Count => _tree.Count;

    /// <summary>Gets the comparer that orders the elements.</summary>
    public IComparer<T> Comparer => _tree.Comparer;

    /// <summary>Gets the smallest element.</summary>
    /// <exception cref="InvalidOperationException">The set is empty.</exception>
    public T Min => _tree.First.Key;

    /// <summary>Gets the largest element.</summary>
    /// <exception cref="InvalidOperationException">The set is empty.</exception>
    public T Max => _tree.Last.Key;

    /// <summary>Gets the element at <paramref name="index"/> in sorted order (0 = smallest). O(log n).</summary>
    /// <param name="index">The position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public T this[int index] => _tree.GetAt(index).Key;

    /// <summary>Adds <paramref name="item"/>.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if added; <see langword="false"/> if already present.</returns>
    public bool Add(T item) => _tree.TryAdd(item, default);

    /// <summary>Removes <paramref name="item"/>.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if it was present.</returns>
    public bool Remove(T item) => _tree.Remove(item);

    /// <summary>Removes the element at <paramref name="index"/> in sorted order. O(log n).</summary>
    /// <param name="index">The position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public void RemoveAt(int index) => _tree.RemoveAt(index);

    /// <summary>Returns whether <paramref name="item"/> is present.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(T item) => _tree.ContainsKey(item);

    /// <summary>Returns the position of <paramref name="item"/> in sorted order, or -1 if absent. O(log n).</summary>
    /// <param name="item">The element.</param>
    /// <returns>The index, or -1.</returns>
    public int IndexOf(T item) => _tree.IndexOfKey(item);

    /// <summary>Returns how many elements are less than <paramref name="item"/> (its rank), present or not. O(log n).</summary>
    /// <param name="item">The value.</param>
    /// <returns>The number of smaller elements.</returns>
    public int CountLessThan(T item) => _tree.CountLessThan(item);

    /// <summary>Gets the largest element &lt;= <paramref name="item"/>.</summary>
    /// <param name="item">The value.</param>
    /// <param name="floor">The element.</param>
    /// <returns><see langword="true"/> if there is one.</returns>
    public bool TryGetFloor(T item, out T floor)
    {
        bool found = _tree.TryGetFloor(item, out KeyValuePair<T, NoValue> entry);
        floor = entry.Key;
        return found;
    }

    /// <summary>Gets the smallest element &gt;= <paramref name="item"/>.</summary>
    /// <param name="item">The value.</param>
    /// <param name="ceiling">The element.</param>
    /// <returns><see langword="true"/> if there is one.</returns>
    public bool TryGetCeiling(T item, out T ceiling)
    {
        bool found = _tree.TryGetCeiling(item, out KeyValuePair<T, NoValue> entry);
        ceiling = entry.Key;
        return found;
    }

    /// <summary>Enumerates the elements in [<paramref name="from"/>, <paramref name="to"/>], in order.</summary>
    /// <param name="from">The smallest value to include.</param>
    /// <param name="to">The largest value to include.</param>
    /// <returns>An allocation-free enumerable.</returns>
    public RangeEnumerable EnumerateRange(T from, T to) => new(_tree.EnumerateRange(from, to));

    /// <summary>Removes all elements.</summary>
    public void Clear() => _tree.Clear();

    /// <summary>Enumerates the elements in sorted order.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(_tree.GetEnumerator());

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Validate() => _tree.Validate();

    /// <summary>Elements in a closed range. See <see cref="EnumerateRange"/>.</summary>
    public readonly struct RangeEnumerable : IEnumerable<T>
    {
        private readonly BTreeDictionary<T, NoValue>.RangeEnumerable _inner;

        internal RangeEnumerable(BTreeDictionary<T, NoValue>.RangeEnumerable inner) => _inner = inner;

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public Enumerator GetEnumerator() => new(_inner.GetEnumerator());

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates a <see cref="BTreeSet{T}"/> in sorted order.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private BTreeDictionary<T, NoValue>.Enumerator _inner;

        internal Enumerator(BTreeDictionary<T, NoValue>.Enumerator inner) => _inner = inner;

        /// <inheritdoc />
        public readonly T Current => _inner.Current.Key;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => _inner.MoveNext();

        /// <inheritdoc />
        public void Reset() => _inner.Reset();

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}

/// <summary>A zero-information value for sets built on dictionaries.</summary>
internal readonly struct NoValue;
