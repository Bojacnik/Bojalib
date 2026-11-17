using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Collections;

/// <summary>
/// A priority queue of unique keys whose priorities can be changed or removed in O(log n) — the operation
/// Dijkstra, A* and Prim need, which <see cref="PriorityQueue{TElement, TPriority}"/> can't do efficiently.
/// The lowest priority is dequeued first. A 4-ary heap plus a key → heap-position map.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TPriority">The priority type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class IndexedPriorityQueue<TKey, TPriority> : IReadOnlyCollection<KeyValuePair<TKey, TPriority>>
    where TKey : notnull
{
    private const int Log2Arity = 2;

    private readonly IComparer<TPriority>? _comparer;
    private readonly Dictionary<TKey, int> _positions;
    private (TKey Key, TPriority Priority)[] _heap = [];
    private int _count;
    private int _version;

    /// <summary>Creates an empty queue.</summary>
    /// <param name="priorityComparer">Orders priorities (lowest first); <see langword="null"/> for the default.</param>
    /// <param name="keyComparer">Compares keys; <see langword="null"/> for the default.</param>
    public IndexedPriorityQueue(IComparer<TPriority>? priorityComparer = null, IEqualityComparer<TKey>? keyComparer = null)
    {
        _comparer = priorityComparer is null || ReferenceEquals(priorityComparer, Comparer<TPriority>.Default) ? null : priorityComparer;
        _positions = new Dictionary<TKey, int>(keyComparer);
    }

    /// <summary>Gets the number of keys.</summary>
    public int Count => _count;

    /// <summary>Gets whether the queue is empty.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Adds <paramref name="key"/> with <paramref name="priority"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">Its priority.</param>
    /// <exception cref="ArgumentException">The key is already in the queue.</exception>
    public void Enqueue(TKey key, TPriority priority)
    {
        if (!TryEnqueue(key, priority))
        {
            throw new ArgumentException($"The key '{key}' is already in the queue.", nameof(key));
        }
    }

    /// <summary>Adds <paramref name="key"/> unless it is already in the queue.</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">Its priority.</param>
    /// <returns><see langword="true"/> if added.</returns>
    public bool TryEnqueue(TKey key, TPriority priority)
    {
        ref int position = ref CollectionsMarshal.GetValueRefOrAddDefault(_positions, key, out bool exists);
        if (exists)
        {
            return false;
        }

        if (_count == _heap.Length)
        {
            Array.Resize(ref _heap, Math.Max(4, _heap.Length * 2));
        }

        position = _count;
        _count++;
        SiftUp(position, (key, priority));
        _version++;
        return true;
    }

    /// <summary>Adds <paramref name="key"/>, or changes its priority if it is already in the queue.</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">The new priority.</param>
    public void EnqueueOrUpdate(TKey key, TPriority priority)
    {
        if (!TryEnqueue(key, priority))
        {
            UpdatePriority(key, priority);
        }
    }

    /// <summary>Changes the priority of <paramref name="key"/>, up or down. O(log n).</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">The new priority.</param>
    /// <exception cref="KeyNotFoundException">The key isn't in the queue.</exception>
    public void UpdatePriority(TKey key, TPriority priority)
    {
        if (!_positions.TryGetValue(key, out int position))
        {
            throw new KeyNotFoundException($"The key '{key}' is not in the queue.");
        }

        // Keep the stored key instance (like Dictionary does), not the one passed in: with a custom key comparer
        // they can be equal but different.
        (TKey storedKey, TPriority old) = _heap[position];
        if (Less(priority, old))
        {
            SiftUp(position, (storedKey, priority));
        }
        else
        {
            SiftDown(position, (storedKey, priority));
        }

        _version++;
    }

    /// <summary>Removes and returns the key with the lowest priority.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">The queue is empty.</exception>
    public TKey Dequeue()
    {
        if (!TryDequeue(out TKey? key, out _))
        {
            ThrowHelper.ThrowEmptyCollection("queue");
        }

        return key;
    }

    /// <summary>Removes the key with the lowest priority, if any.</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">Its priority.</param>
    /// <returns><see langword="true"/> if a key was removed.</returns>
    public bool TryDequeue([MaybeNullWhen(false)] out TKey key, [MaybeNullWhen(false)] out TPriority priority)
    {
        if (_count == 0)
        {
            key = default;
            priority = default;
            return false;
        }

        (key, priority) = _heap[0];
        RemoveAt(0);
        return true;
    }

    /// <summary>Returns the key with the lowest priority without removing it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">Its priority.</param>
    /// <returns><see langword="true"/> if the queue has a key.</returns>
    public bool TryPeek([MaybeNullWhen(false)] out TKey key, [MaybeNullWhen(false)] out TPriority priority)
    {
        if (_count == 0)
        {
            key = default;
            priority = default;
            return false;
        }

        (key, priority) = _heap[0];
        return true;
    }

    /// <summary>Removes <paramref name="key"/> from anywhere in the queue. O(log n).</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if it was in the queue.</returns>
    public bool Remove(TKey key)
    {
        if (!_positions.TryGetValue(key, out int position))
        {
            return false;
        }

        RemoveAt(position);
        return true;
    }

    /// <summary>Returns whether <paramref name="key"/> is in the queue.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(TKey key) => _positions.ContainsKey(key);

    /// <summary>Gets the priority of <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="priority">Its priority.</param>
    /// <returns><see langword="true"/> if the key is in the queue.</returns>
    public bool TryGetPriority(TKey key, [MaybeNullWhen(false)] out TPriority priority)
    {
        if (_positions.TryGetValue(key, out int position))
        {
            priority = _heap[position].Priority;
            return true;
        }

        priority = default;
        return false;
    }

    /// <summary>Removes all keys.</summary>
    public void Clear()
    {
        Array.Clear(_heap, 0, _count);
        _positions.Clear();
        _count = 0;
        _version++;
    }

    /// <summary>Enumerates the keys and priorities in heap order (not sorted).</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<KeyValuePair<TKey, TPriority>> GetEnumerator()
    {
        int version = _version;
        for (int i = 0; i < _count; i++)
        {
            yield return new(_heap[i].Key, _heap[i].Priority);
            if (version != _version)
            {
                ThrowHelper.ThrowCollectionModified();
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Less(TPriority x, TPriority y) =>
        _comparer is null ? default(DefaultLess<TPriority>).Less(x, y) : _comparer.Compare(x, y) < 0;

    private void RemoveAt(int position)
    {
        _positions.Remove(_heap[position].Key);
        _count--;
        (TKey Key, TPriority Priority) last = _heap[_count];
        _heap[_count] = default;

        if (position < _count)
        {
            if (Less(last.Priority, _heap[position].Priority))
            {
                SiftUp(position, last);
            }
            else
            {
                SiftDown(position, last);
            }
        }

        _version++;
    }

    private void SiftUp(int index, (TKey Key, TPriority Priority) item)
    {
        while (index > 0)
        {
            int parent = (index - 1) >> Log2Arity;
            if (!Less(item.Priority, _heap[parent].Priority))
            {
                break;
            }

            Place(index, _heap[parent]);
            index = parent;
        }

        Place(index, item);
    }

    private void SiftDown(int index, (TKey Key, TPriority Priority) item)
    {
        while (true)
        {
            int first = (index << Log2Arity) + 1;
            if (first >= _count)
            {
                break;
            }

            int smallest = first;
            int last = Math.Min(first + 4, _count);
            for (int child = first + 1; child < last; child++)
            {
                if (Less(_heap[child].Priority, _heap[smallest].Priority))
                {
                    smallest = child;
                }
            }

            if (!Less(_heap[smallest].Priority, item.Priority))
            {
                break;
            }

            Place(index, _heap[smallest]);
            index = smallest;
        }

        Place(index, item);
    }

    private void Place(int index, (TKey Key, TPriority Priority) item)
    {
        _heap[index] = item;
        _positions[item.Key] = index;
    }
}
