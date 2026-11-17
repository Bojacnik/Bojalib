using System.Diagnostics;
using Bojalib.Collections;

namespace Bojalib.Probabilistic;

/// <summary>
/// Finds the most frequent items in a stream using fixed memory (the Space-Saving algorithm, Metwally et al.):
/// "top 100 URLs", "noisiest clients". Unlike <see cref="CountMinSketch{T}"/>, it tells you <em>which</em> items are
/// frequent, with a guaranteed error bound on each count.
/// </summary>
/// <remarks>
/// <para>
/// Tracks <see cref="Capacity"/> counters. A new item replaces the item with the smallest count and inherits that
/// count as its possible error. Guarantees: every item whose true count exceeds
/// <see cref="TotalCount"/> / <see cref="Capacity"/> is tracked, and each tracked item's true count lies in
/// [<c>Count − Error</c>, <c>Count</c>]. Use a capacity a few times larger than the number of results you need.
/// </para>
/// <para>Each update is O(log capacity). Not thread-safe.</para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("Tracked = {TrackedCount}, Capacity = {Capacity}")]
public sealed class TopK<T>
    where T : notnull
{
    private static readonly IComparer<Counter> s_byCount = Comparer<Counter>.Create(static (a, b) => a.Count.CompareTo(b.Count));

    private readonly IndexedPriorityQueue<T, Counter> _counters;

    /// <summary>Creates an empty tracker.</summary>
    /// <param name="capacity">How many items to track.</param>
    /// <param name="comparer">Compares items; <see langword="null"/> for the default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public TopK(int capacity, IEqualityComparer<T>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _counters = new IndexedPriorityQueue<T, Counter>(s_byCount, comparer);
    }

    /// <summary>Gets how many items are tracked at most.</summary>
    public int Capacity { get; }

    /// <summary>Gets how many items are currently tracked.</summary>
    public int TrackedCount => _counters.Count;

    /// <summary>Gets the sum of all counts added.</summary>
    public long TotalCount { get; private set; }

    /// <summary>Records <paramref name="count"/> occurrences of <paramref name="item"/>.</summary>
    /// <param name="item">The item.</param>
    /// <param name="count">How many occurrences; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than 1.</exception>
    public void Add(T item, long count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        TotalCount += count;

        if (_counters.TryGetPriority(item, out Counter current))
        {
            _counters.UpdatePriority(item, current with { Count = current.Count + count });
        }
        else if (_counters.Count < Capacity)
        {
            _counters.Enqueue(item, new Counter(count, 0));
        }
        else
        {
            // Evict the least counted item; the newcomer may have occurred up to that many times unseen.
            _counters.TryDequeue(out _, out Counter evicted);
            _counters.Enqueue(item, new Counter(evicted.Count + count, evicted.Count));
        }
    }

    /// <summary>Gets the estimated count of <paramref name="item"/>, if it is tracked.</summary>
    /// <param name="item">The item.</param>
    /// <param name="count">An upper bound on its true count.</param>
    /// <param name="error">How much <paramref name="count"/> may overstate it.</param>
    /// <returns><see langword="true"/> if the item is tracked.</returns>
    public bool TryGetCount(T item, out long count, out long error)
    {
        bool tracked = _counters.TryGetPriority(item, out Counter counter);
        (count, error) = (counter.Count, counter.Error);
        return tracked;
    }

    /// <summary>Returns the <paramref name="n"/> most frequent tracked items, most frequent first.</summary>
    /// <param name="n">How many to return (at most <see cref="TrackedCount"/>).</param>
    /// <returns>The items with their estimated counts and error bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
    public Entry[] GetTop(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        Entry[] all = [.. _counters.Select(static pair => new Entry(pair.Key, pair.Value.Count, pair.Value.Error))];
        Array.Sort(all, static (a, b) => b.Count.CompareTo(a.Count));
        return all.Length <= n ? all : all[..n];
    }

    /// <summary>Removes all data.</summary>
    public void Clear()
    {
        _counters.Clear();
        TotalCount = 0;
    }

    /// <summary>A tracked item with its estimated count.</summary>
    /// <param name="Item">The item.</param>
    /// <param name="Count">An upper bound on its true count.</param>
    /// <param name="Error">How much <paramref name="Count"/> may overstate the true count.</param>
    public readonly record struct Entry(T Item, long Count, long Error);

    private readonly record struct Counter(long Count, long Error);
}
