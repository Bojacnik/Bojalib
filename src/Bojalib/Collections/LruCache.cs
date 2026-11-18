using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bojalib.Collections;

/// <summary>
/// Single-threaded, allocation-free least-recently-used cache: when full, adding evicts the entry that was used
/// longest ago. Every operation is O(1) — one hash lookup plus a few index writes — and once full it never allocates.
/// </summary>
/// <remarks>
/// <para>
/// Built for hot single-threaded paths (parsers, compilers, game loops, per-request work). It is <b>not</b>
/// thread-safe; for a cache shared between threads use a concurrent cache library such as BitFaster.Caching, or
/// <c>Microsoft.Extensions.Caching.Memory</c> when you need expiry.
/// </para>
/// <para>
/// Eviction is exact LRU. Reading with <see cref="TryGetValue(TKey, out TValue)"/> or the indexer counts as a use;
/// <see cref="TryPeek"/> and <see cref="ContainsKey"/> don't. String-keyed caches can be read with a
/// <see cref="ReadOnlySpan{T}"/> key without allocating. Enumeration goes from most to least recently used.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
[DebuggerDisplay("Count = {Count}, Capacity = {Capacity}")]
public sealed class LruCache<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private const int None = -1;

    private readonly Dictionary<TKey, int> _slots;
    private readonly Entry[] _entries;
    private readonly Action<TKey, TValue>? _onEvicted;
    private int _head = None; // most recently used
    private int _tail = None; // least recently used
    private int _count;
    private int _version;

    /// <summary>Creates an empty cache.</summary>
    /// <param name="capacity">The maximum number of entries.</param>
    /// <param name="comparer">Compares keys; <see langword="null"/> for the default.</param>
    /// <param name="onEvicted">Called with each entry evicted to make room (not for explicit removals).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null, Action<TKey, TValue>? onEvicted = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new Dictionary<TKey, int>(capacity + 1, comparer);
        _entries = new Entry[capacity];
        _onEvicted = onEvicted;
    }

    /// <summary>Gets the maximum number of entries.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Gets the number of entries.</summary>
    public int Count => _count;

    /// <summary>Gets or sets the value for <paramref name="key"/>; both count as a use. Setting adds or replaces.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="KeyNotFoundException">Getting a key that isn't cached.</exception>
    public TValue this[TKey key]
    {
        get => TryGetValue(key, out TValue? value) ? value : throw new KeyNotFoundException($"The key '{key}' is not in the cache.");
        set => AddOrUpdate(key, value);
    }

    /// <summary>Adds or replaces <paramref name="key"/>'s value and marks it most recently used, evicting the least recently used entry if full.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void AddOrUpdate(TKey key, TValue value)
    {
        // One hash lookup for both hits and misses. The dictionary has room for Capacity + 1 keys, so adding before
        // the victim is removed never resizes it, and removing another key doesn't move this entry.
        ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_slots, key, out bool exists);
        if (exists)
        {
            _entries[slot].Value = value;
            MoveToFront(slot);
            _version++;
            return;
        }

        bool evicted = false;
        TKey evictedKey = default!;
        TValue evictedValue = default!;
        if (_count < _entries.Length)
        {
            slot = _count++;
        }
        else
        {
            int victim = _tail;
            Unlink(victim);
            evicted = true;
            evictedKey = _entries[victim].Key;
            evictedValue = _entries[victim].Value;
            slot = victim;
            _slots.Remove(evictedKey);
        }

        ref Entry entry = ref _entries[slot];
        entry.Key = key;
        entry.Value = value;
        LinkFront(slot);
        _version++;

        // Last, so a callback that uses the cache sees a consistent state.
        if (evicted)
        {
            _onEvicted?.Invoke(evictedKey, evictedValue);
        }
    }

    /// <summary>Gets the value for <paramref name="key"/> and marks it most recently used.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if cached.</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_slots.TryGetValue(key, out int slot))
        {
            MoveToFront(slot);
            value = _entries[slot].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the value using an alternate key type — e.g. a <see cref="ReadOnlySpan{T}"/> of <see langword="char"/>
    /// for a <see langword="string"/>-keyed cache — without allocating a key. Marks the entry most recently used.
    /// </summary>
    /// <typeparam name="TAlternateKey">The alternate key type; the cache's comparer must support it.</typeparam>
    /// <param name="key">The alternate key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if cached.</returns>
    /// <exception cref="InvalidOperationException">The comparer doesn't support <typeparamref name="TAlternateKey"/>.</exception>
    public bool TryGetValue<TAlternateKey>(TAlternateKey key, [MaybeNullWhen(false)] out TValue value)
        where TAlternateKey : notnull, allows ref struct
    {
        Dictionary<TKey, int>.AlternateLookup<TAlternateKey> lookup = _slots.GetAlternateLookup<TAlternateKey>();
        if (lookup.TryGetValue(key, out int slot))
        {
            MoveToFront(slot);
            value = _entries[slot].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the value for <paramref name="key"/> without counting it as a use.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if cached.</returns>
    public bool TryPeek(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_slots.TryGetValue(key, out int slot))
        {
            value = _entries[slot].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the cached value, or creates, caches and returns one.</summary>
    /// <param name="key">The key.</param>
    /// <param name="factory">Creates the value on a miss.</param>
    /// <returns>The cached or new value.</returns>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (TryGetValue(key, out TValue? value))
        {
            return value;
        }

        value = factory(key);
        AddOrUpdate(key, value);
        return value;
    }

    /// <summary>Returns whether <paramref name="key"/> is cached, without counting it as a use.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if cached.</returns>
    public bool ContainsKey(TKey key) => _slots.ContainsKey(key);

    /// <summary>Removes <paramref name="key"/>. Doesn't call the eviction callback.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if it was cached.</returns>
    public bool Remove(TKey key)
    {
        if (!_slots.Remove(key, out int slot))
        {
            return false;
        }

        Unlink(slot);

        // Keep entries dense: move the last used slot into the hole.
        int last = _count - 1;
        if (slot != last)
        {
            _entries[slot] = _entries[last];
            _slots[_entries[slot].Key] = slot;
            ref Entry moved = ref _entries[slot];
            if (moved.Previous != None)
            {
                _entries[moved.Previous].Next = slot;
            }
            else
            {
                _head = slot;
            }

            if (moved.Next != None)
            {
                _entries[moved.Next].Previous = slot;
            }
            else
            {
                _tail = slot;
            }
        }

        _entries[last] = default;
        _count--;
        _version++;
        return true;
    }

    /// <summary>Removes all entries. Doesn't call the eviction callback.</summary>
    public void Clear()
    {
        _slots.Clear();
        Array.Clear(_entries, 0, _count);
        _head = _tail = None;
        _count = 0;
        _version++;
    }

    /// <summary>Enumerates entries from most to least recently used, without counting them as uses.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void MoveToFront(int slot)
    {
        if (slot != _head)
        {
            Unlink(slot);
            LinkFront(slot);
        }
    }

    private void LinkFront(int slot)
    {
        ref Entry entry = ref _entries[slot];
        entry.Previous = None;
        entry.Next = _head;
        if (_head != None)
        {
            _entries[_head].Previous = slot;
        }

        _head = slot;
        if (_tail == None)
        {
            _tail = slot;
        }
    }

    private void Unlink(int slot)
    {
        ref Entry entry = ref _entries[slot];
        if (entry.Previous != None)
        {
            _entries[entry.Previous].Next = entry.Next;
        }
        else
        {
            _head = entry.Next;
        }

        if (entry.Next != None)
        {
            _entries[entry.Next].Previous = entry.Previous;
        }
        else
        {
            _tail = entry.Previous;
        }
    }

    private struct Entry
    {
        public TKey Key;
        public TValue Value;
        public int Previous;
        public int Next;
    }

    /// <summary>Enumerates an <see cref="LruCache{TKey, TValue}"/> from most to least recently used.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly LruCache<TKey, TValue> _cache;
        private readonly int _version;
        private int _slot;
        private bool _started;

        internal Enumerator(LruCache<TKey, TValue> cache)
        {
            _cache = cache;
            _version = cache._version;
            _slot = None;
            _started = false;
        }

        /// <inheritdoc />
        public readonly KeyValuePair<TKey, TValue> Current => new(_cache._entries[_slot].Key, _cache._entries[_slot].Value);

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _cache._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            _slot = _started ? (_slot == None ? None : _cache._entries[_slot].Next) : _cache._head;
            _started = true;
            return _slot != None;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _slot = None;
            _started = false;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
