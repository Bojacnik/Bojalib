using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Bojalib.Collections.Internal;
using Bojalib.Sorting.Internal;

namespace Bojalib.Collections;

/// <summary>
/// A sorted dictionary backed by a B+tree that also knows positions: besides O(log n) add, remove and lookup, it
/// answers "what is the 1000th entry?" (<see cref="GetAt"/>), "how many keys are smaller than this?"
/// (<see cref="CountLessThan"/>) and floor/ceiling/range queries — none of which
/// <see cref="SortedDictionary{TKey, TValue}"/> can do efficiently.
/// </summary>
/// <remarks>
/// <para>
/// Nodes hold up to 64 entries, so the tree is shallow (4 levels for tens of millions of entries) and each node is a
/// few cache lines of contiguous keys. Internal nodes record the size of every child subtree, which is what makes
/// positional queries O(log n). Leaves are linked, so in-order and range enumeration walk arrays sequentially and
/// allocate nothing. For built-in integer keys with the default comparer, the search inside a node is SIMD.
/// </para>
/// <para>Not thread-safe. Keys must not be <see langword="null"/>.</para>
/// </remarks>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class BTreeDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private const int LeafCapacity = 64;
    private const int InternalCapacity = 64;
    private const int MinLeaf = LeafCapacity / 2;
    private const int MinInternal = InternalCapacity / 2;

    private readonly IComparer<TKey>? _comparer; // null = default ordering (fast path)
    private readonly bool _vectorSearch;
    private Node _root;
    private Leaf _first;
    private Leaf _last;
    private int _count;
    private int _version;

    /// <summary>Creates an empty dictionary.</summary>
    /// <param name="comparer">Orders the keys; <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public BTreeDictionary(IComparer<TKey>? comparer = null)
    {
        // Floats keep the comparer so NaN is ordered like Comparer<T>.Default rather than with a raw '<'.
        bool isDefault = comparer is null || ReferenceEquals(comparer, Comparer<TKey>.Default);
        _comparer = isDefault && !Primitives.IsFloatingPoint<TKey>() ? null : (comparer ?? Comparer<TKey>.Default);
        _vectorSearch = _comparer is null && VectorSearch.IsSupported<TKey>();
        _root = _first = _last = new Leaf();
    }

    /// <summary>Gets the number of entries.</summary>
    public int Count => _count;

    /// <summary>Gets the comparer that orders the keys.</summary>
    public IComparer<TKey> Comparer => _comparer ?? Comparer<TKey>.Default;

    /// <summary>Gets all keys, in order.</summary>
    public IEnumerable<TKey> Keys => this.Select(static pair => pair.Key);

    /// <summary>Gets all values, in key order.</summary>
    public IEnumerable<TValue> Values => this.Select(static pair => pair.Value);

    /// <summary>Gets the entry with the smallest key.</summary>
    /// <exception cref="InvalidOperationException">The dictionary is empty.</exception>
    public KeyValuePair<TKey, TValue> First => _count == 0 ? ThrowEmpty() : new(_first.Keys[0], _first.Values[0]);

    /// <summary>Gets the entry with the largest key.</summary>
    /// <exception cref="InvalidOperationException">The dictionary is empty.</exception>
    public KeyValuePair<TKey, TValue> Last => _count == 0 ? ThrowEmpty() : new(_last.Keys[_last.Count - 1], _last.Values[_last.Count - 1]);

    /// <summary>Gets or sets the value for <paramref name="key"/>. Setting adds the key or replaces its value.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="KeyNotFoundException">Getting a key that isn't present.</exception>
    public TValue this[TKey key]
    {
        get => TryGetValue(key, out TValue? value) ? value : throw new KeyNotFoundException($"The key '{key}' was not found.");
        set => Insert(key, value, overwrite: true);
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The key is already present.</exception>
    public void Add(TKey key, TValue value)
    {
        if (!Insert(key, value, overwrite: false))
        {
            throw new ArgumentException($"An item with the key '{key}' has already been added.", nameof(key));
        }
    }

    /// <summary>Adds <paramref name="key"/> unless it is already present.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if added.</returns>
    public bool TryAdd(TKey key, TValue value) => Insert(key, value, overwrite: false);

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ThrowHelper.ThrowIfNull(key);
        Leaf leaf = FindLeaf(key);
        int index = LowerBound(leaf.Keys, 0, leaf.Count, key);
        if (index < leaf.Count && !Less(key, leaf.Keys[index]))
        {
            value = leaf.Values[index];
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Returns whether <paramref name="key"/> is present.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    /// <summary>Removes <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if it was present.</returns>
    public bool Remove(TKey key) => Remove(key, out _);

    /// <summary>Removes <paramref name="key"/> and returns its value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The removed value.</param>
    /// <returns><see langword="true"/> if it was present.</returns>
    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ThrowHelper.ThrowIfNull(key);
        if (!RemoveFrom(_root, key, out value))
        {
            return false;
        }

        // Shrink the tree when the root has a single child.
        while (_root is Internal { Count: 1 } onlyChild)
        {
            _root = onlyChild.Children[0];
        }

        _count--;
        _version++;
        return true;
    }

    /// <summary>Returns the entry at <paramref name="index"/> in key order. O(log n).</summary>
    /// <param name="index">The position, 0 for the smallest key.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public KeyValuePair<TKey, TValue> GetAt(int index)
    {
        (Leaf leaf, int position) = Locate(index);
        return new(leaf.Keys[position], leaf.Values[position]);
    }

    /// <summary>Removes the entry at <paramref name="index"/> in key order. O(log n).</summary>
    /// <param name="index">The position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public void RemoveAt(int index) => Remove(GetAt(index).Key);

    /// <summary>Returns the position of <paramref name="key"/> in key order, or -1 if it isn't present. O(log n).</summary>
    /// <param name="key">The key.</param>
    /// <returns>The index, or -1.</returns>
    public int IndexOfKey(TKey key)
    {
        int rank = Rank(key, out bool found);
        return found ? rank : -1;
    }

    /// <summary>Returns how many keys are less than <paramref name="key"/> (its rank), whether or not it is present. O(log n).</summary>
    /// <param name="key">The key.</param>
    /// <returns>The number of smaller keys.</returns>
    public int CountLessThan(TKey key) => Rank(key, out _);

    /// <summary>Gets the entry with the largest key that is &lt;= <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> if there is one.</returns>
    public bool TryGetFloor(TKey key, out KeyValuePair<TKey, TValue> entry)
    {
        ThrowHelper.ThrowIfNull(key);
        Leaf leaf = FindLeaf(key);
        int index = UpperBound(leaf.Keys, 0, leaf.Count, key) - 1;
        if (index < 0)
        {
            leaf = leaf.Previous!;
            index = leaf is null ? -1 : leaf.Count - 1;
        }

        entry = index >= 0 ? new(leaf!.Keys[index], leaf.Values[index]) : default;
        return index >= 0;
    }

    /// <summary>Gets the entry with the smallest key that is &gt;= <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> if there is one.</returns>
    public bool TryGetCeiling(TKey key, out KeyValuePair<TKey, TValue> entry)
    {
        (Leaf? leaf, int index) = Ceiling(key);
        entry = leaf is null ? default : new(leaf.Keys[index], leaf.Values[index]);
        return leaf is not null;
    }

    /// <summary>Enumerates the entries with keys in [<paramref name="from"/>, <paramref name="to"/>], in key order.</summary>
    /// <param name="from">The smallest key to include.</param>
    /// <param name="to">The largest key to include.</param>
    /// <returns>An allocation-free enumerable.</returns>
    public RangeEnumerable EnumerateRange(TKey from, TKey to)
    {
        ThrowHelper.ThrowIfNull(to);
        (Leaf? leaf, int index) = Ceiling(from);
        return new RangeEnumerable(this, leaf, index, to, bounded: true);
    }

    /// <summary>Removes all entries.</summary>
    public void Clear()
    {
        _root = _first = _last = new Leaf();
        _count = 0;
        _version++;
    }

    /// <summary>Enumerates all entries in key order.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this, _first, 0, default!, bounded: false);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Checks every structural invariant; used by tests.</summary>
    internal void Validate()
    {
        int depth = -1;
        int total = ValidateNode(_root, isRoot: true, 0, ref depth, hasLow: false, low: default!, hasHigh: false, high: default!);
        Debug.Assert(total == _count, "Size mismatch");
        if (total != _count)
        {
            throw new InvalidOperationException($"Count {_count} but tree holds {total}.");
        }

        int linked = 0;
        Leaf? previous = null;
        for (Leaf? leaf = _first; leaf is not null; leaf = leaf.Next)
        {
            if (leaf.Previous != previous)
            {
                throw new InvalidOperationException("Broken leaf links.");
            }

            linked += leaf.Count;
            previous = leaf;
        }

        if (linked != _count || previous != _last)
        {
            throw new InvalidOperationException("Leaf chain doesn't cover the tree.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Less(TKey x, TKey y) => _comparer is null ? default(DefaultLess<TKey>).Less(x, y) : _comparer.Compare(x, y) < 0;

    // First position whose key is not less than `key`.
    private int LowerBound(TKey[] keys, int start, int end, TKey key)
    {
        if (_vectorSearch)
        {
            return start + VectorSearch.Count(keys, start, end - start, key, orEqual: false);
        }

        while (start < end)
        {
            int middle = (int)((uint)(start + end) >> 1);
            if (Less(keys[middle], key))
            {
                start = middle + 1;
            }
            else
            {
                end = middle;
            }
        }

        return start;
    }

    // First position whose key is greater than `key`.
    private int UpperBound(TKey[] keys, int start, int end, TKey key)
    {
        if (_vectorSearch)
        {
            return start + VectorSearch.Count(keys, start, end - start, key, orEqual: true);
        }

        while (start < end)
        {
            int middle = (int)((uint)(start + end) >> 1);
            if (Less(key, keys[middle]))
            {
                end = middle;
            }
            else
            {
                start = middle + 1;
            }
        }

        return start;
    }

    // Child of `node` whose range contains `key`: separators are Keys[1..Count), Keys[i] = smallest key under child i.
    private int ChildIndex(Internal node, TKey key) => UpperBound(node.Keys, 1, node.Count, key) - 1;

    private Leaf FindLeaf(TKey key)
    {
        Node node = _root;
        while (node is Internal parent)
        {
            node = parent.Children[ChildIndex(parent, key)];
        }

        return (Leaf)node;
    }

    private (Leaf? Leaf, int Index) Ceiling(TKey key)
    {
        ThrowHelper.ThrowIfNull(key);
        Leaf leaf = FindLeaf(key);
        int index = LowerBound(leaf.Keys, 0, leaf.Count, key);
        if (index == leaf.Count)
        {
            return leaf.Next is null ? (null, 0) : (leaf.Next, 0);
        }

        return (leaf, index);
    }

    private (Leaf Leaf, int Index) Locate(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
        Node node = _root;
        while (node is Internal parent)
        {
            int child = 0;
            while (index >= parent.ChildSizes[child])
            {
                index -= parent.ChildSizes[child];
                child++;
            }

            node = parent.Children[child];
        }

        return ((Leaf)node, index);
    }

    private int Rank(TKey key, out bool found)
    {
        ThrowHelper.ThrowIfNull(key);
        int rank = 0;
        Node node = _root;
        while (node is Internal parent)
        {
            int child = ChildIndex(parent, key);
            for (int i = 0; i < child; i++)
            {
                rank += parent.ChildSizes[i];
            }

            node = parent.Children[child];
        }

        var leaf = (Leaf)node;
        int index = LowerBound(leaf.Keys, 0, leaf.Count, key);
        found = index < leaf.Count && !Less(key, leaf.Keys[index]);
        return rank + index;
    }

    private bool Insert(TKey key, TValue value, bool overwrite)
    {
        ThrowHelper.ThrowIfNull(key);
        (Node? right, TKey separator) = InsertInto(_root, key, value, overwrite, out bool added);
        if (right is not null)
        {
            var root = new Internal { Count = 2 };
            root.Children[0] = _root;
            root.Children[1] = right;
            root.Keys[1] = separator;
            root.ChildSizes[0] = Size(_root);
            root.ChildSizes[1] = Size(right);
            _root = root;
        }

        if (added)
        {
            _count++;
            _version++;
        }
        else if (overwrite)
        {
            _version++;
        }

        return added;
    }

    // Inserts into the subtree; if the node had to split, returns the new right sibling and its smallest key.
    private (Node? Right, TKey Separator) InsertInto(Node node, TKey key, TValue value, bool overwrite, out bool added)
    {
        if (node is Leaf leaf)
        {
            int index = LowerBound(leaf.Keys, 0, leaf.Count, key);
            if (index < leaf.Count && !Less(key, leaf.Keys[index]))
            {
                if (overwrite)
                {
                    leaf.Values[index] = value;
                }

                added = false;
                return (null, default!);
            }

            added = true;
            if (leaf.Count < LeafCapacity)
            {
                InsertAt(leaf, index, key, value);
                return (null, default!);
            }

            Leaf right = SplitLeaf(leaf);
            if (index <= leaf.Count)
            {
                InsertAt(leaf, index, key, value);
            }
            else
            {
                InsertAt(right, index - leaf.Count, key, value);
            }

            return (right, right.Keys[0]);
        }

        var parent = (Internal)node;
        int child = ChildIndex(parent, key);
        (Node? newChild, TKey childSeparator) = InsertInto(parent.Children[child], key, value, overwrite, out added);
        if (newChild is null)
        {
            if (added)
            {
                parent.ChildSizes[child]++;
            }

            return (null, default!);
        }

        // The child split: fix its size and insert the new sibling right after it.
        parent.ChildSizes[child] = Size(parent.Children[child]);
        int newChildSize = Size(newChild);
        if (parent.Count < InternalCapacity)
        {
            InsertChild(parent, child + 1, newChild, childSeparator, newChildSize);
            return (null, default!);
        }

        (Internal rightNode, TKey promoted) = SplitInternal(parent);
        if (child + 1 <= parent.Count)
        {
            InsertChild(parent, child + 1, newChild, childSeparator, newChildSize);
        }
        else
        {
            InsertChild(rightNode, child + 1 - parent.Count, newChild, childSeparator, newChildSize);
        }

        return (rightNode, promoted);
    }

    private bool RemoveFrom(Node node, TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (node is Leaf leaf)
        {
            int index = LowerBound(leaf.Keys, 0, leaf.Count, key);
            if (index == leaf.Count || Less(key, leaf.Keys[index]))
            {
                value = default;
                return false;
            }

            value = leaf.Values[index];
            RemoveAt(leaf, index);
            return true;
        }

        var parent = (Internal)node;
        int child = ChildIndex(parent, key);
        if (!RemoveFrom(parent.Children[child], key, out value))
        {
            return false;
        }

        parent.ChildSizes[child]--;
        Node childNode = parent.Children[child];
        int minimum = childNode is Leaf ? MinLeaf : MinInternal;
        if (childNode.Count < minimum && parent.Count > 1)
        {
            Rebalance(parent, child);
        }

        return true;
    }

    // `child` is under-full: borrow from a sibling that can spare an entry, otherwise merge with a sibling.
    private void Rebalance(Internal parent, int child)
    {
        Node node = parent.Children[child];
        int minimum = node is Leaf ? MinLeaf : MinInternal;

        if (child > 0 && parent.Children[child - 1].Count > minimum)
        {
            BorrowFromLeft(parent, child);
        }
        else if (child + 1 < parent.Count && parent.Children[child + 1].Count > minimum)
        {
            BorrowFromRight(parent, child);
        }
        else if (child > 0)
        {
            Merge(parent, child - 1);
        }
        else
        {
            Merge(parent, child);
        }
    }

    private static void BorrowFromLeft(Internal parent, int child)
    {
        Node node = parent.Children[child];
        Node left = parent.Children[child - 1];
        int moved;
        if (node is Leaf leaf)
        {
            var leftLeaf = (Leaf)left;
            int last = leftLeaf.Count - 1;
            InsertAt(leaf, 0, leftLeaf.Keys[last], leftLeaf.Values[last]);
            RemoveAt(leftLeaf, last);
            parent.Keys[child] = leaf.Keys[0];
            moved = 1;
        }
        else
        {
            var internalNode = (Internal)node;
            var leftInternal = (Internal)left;
            int last = leftInternal.Count - 1;
            moved = leftInternal.ChildSizes[last];

            // The parent's separator moves down; the left sibling's last separator moves up.
            ShiftRight(internalNode, 0);
            internalNode.Children[0] = leftInternal.Children[last];
            internalNode.ChildSizes[0] = moved;
            internalNode.Keys[1] = parent.Keys[child];
            parent.Keys[child] = leftInternal.Keys[last];
            ClearSlot(leftInternal, last);
            leftInternal.Count--;
        }

        parent.ChildSizes[child - 1] -= moved;
        parent.ChildSizes[child] += moved;
    }

    private static void BorrowFromRight(Internal parent, int child)
    {
        Node node = parent.Children[child];
        Node right = parent.Children[child + 1];
        int moved;
        if (node is Leaf leaf)
        {
            var rightLeaf = (Leaf)right;
            InsertAt(leaf, leaf.Count, rightLeaf.Keys[0], rightLeaf.Values[0]);
            RemoveAt(rightLeaf, 0);
            parent.Keys[child + 1] = rightLeaf.Keys[0];
            moved = 1;
        }
        else
        {
            var internalNode = (Internal)node;
            var rightInternal = (Internal)right;
            moved = rightInternal.ChildSizes[0];

            int end = internalNode.Count;
            internalNode.Children[end] = rightInternal.Children[0];
            internalNode.ChildSizes[end] = moved;
            internalNode.Keys[end] = parent.Keys[child + 1];
            internalNode.Count++;
            parent.Keys[child + 1] = rightInternal.Keys[1];
            ShiftLeft(rightInternal, 0);
        }

        parent.ChildSizes[child + 1] -= moved;
        parent.ChildSizes[child] += moved;
    }

    // Merges child `index + 1` into child `index` and removes it from the parent.
    private void Merge(Internal parent, int index)
    {
        Node left = parent.Children[index];
        Node right = parent.Children[index + 1];
        if (left is Leaf leftLeaf)
        {
            var rightLeaf = (Leaf)right;
            Array.Copy(rightLeaf.Keys, 0, leftLeaf.Keys, leftLeaf.Count, rightLeaf.Count);
            Array.Copy(rightLeaf.Values, 0, leftLeaf.Values, leftLeaf.Count, rightLeaf.Count);
            leftLeaf.Count += rightLeaf.Count;
            leftLeaf.Next = rightLeaf.Next;
            if (rightLeaf.Next is not null)
            {
                rightLeaf.Next.Previous = leftLeaf;
            }
            else
            {
                _last = leftLeaf;
            }
        }
        else
        {
            var leftInternal = (Internal)left;
            var rightInternal = (Internal)right;
            int offset = leftInternal.Count;
            Array.Copy(rightInternal.Children, 0, leftInternal.Children, offset, rightInternal.Count);
            Array.Copy(rightInternal.ChildSizes, 0, leftInternal.ChildSizes, offset, rightInternal.Count);
            Array.Copy(rightInternal.Keys, 1, leftInternal.Keys, offset + 1, rightInternal.Count - 1);
            leftInternal.Keys[offset] = parent.Keys[index + 1];
            leftInternal.Count += rightInternal.Count;
        }

        parent.ChildSizes[index] += parent.ChildSizes[index + 1];
        ShiftLeft(parent, index + 1);
    }

    private Leaf SplitLeaf(Leaf leaf)
    {
        var right = new Leaf();
        int half = leaf.Count / 2;
        int moving = leaf.Count - half;
        Array.Copy(leaf.Keys, half, right.Keys, 0, moving);
        Array.Copy(leaf.Values, half, right.Values, 0, moving);
        Array.Clear(leaf.Keys, half, moving);
        Array.Clear(leaf.Values, half, moving);
        right.Count = moving;
        leaf.Count = half;

        right.Next = leaf.Next;
        right.Previous = leaf;
        if (leaf.Next is not null)
        {
            leaf.Next.Previous = right;
        }
        else
        {
            _last = right;
        }

        leaf.Next = right;
        return right;
    }

    private static (Internal Right, TKey Promoted) SplitInternal(Internal node)
    {
        var right = new Internal();
        int half = node.Count / 2;
        int moving = node.Count - half;
        TKey promoted = node.Keys[half];
        Array.Copy(node.Children, half, right.Children, 0, moving);
        Array.Copy(node.ChildSizes, half, right.ChildSizes, 0, moving);
        Array.Copy(node.Keys, half + 1, right.Keys, 1, moving - 1);
        Array.Clear(node.Children, half, moving);
        Array.Clear(node.ChildSizes, half, moving);
        Array.Clear(node.Keys, half, moving);
        right.Count = moving;
        node.Count = half;
        return (right, promoted);
    }

    private static void InsertAt(Leaf leaf, int index, TKey key, TValue value)
    {
        Array.Copy(leaf.Keys, index, leaf.Keys, index + 1, leaf.Count - index);
        Array.Copy(leaf.Values, index, leaf.Values, index + 1, leaf.Count - index);
        leaf.Keys[index] = key;
        leaf.Values[index] = value;
        leaf.Count++;
    }

    private static void RemoveAt(Leaf leaf, int index)
    {
        leaf.Count--;
        Array.Copy(leaf.Keys, index + 1, leaf.Keys, index, leaf.Count - index);
        Array.Copy(leaf.Values, index + 1, leaf.Values, index, leaf.Count - index);
        leaf.Keys[leaf.Count] = default!;
        leaf.Values[leaf.Count] = default!;
    }

    private static void InsertChild(Internal node, int index, Node child, TKey separator, int size)
    {
        ShiftRight(node, index);
        node.Children[index] = child;
        node.ChildSizes[index] = size;
        node.Keys[index] = separator;
    }

    // Opens a gap at `index` (children, sizes and separators move right by one).
    private static void ShiftRight(Internal node, int index)
    {
        Array.Copy(node.Children, index, node.Children, index + 1, node.Count - index);
        Array.Copy(node.ChildSizes, index, node.ChildSizes, index + 1, node.Count - index);
        Array.Copy(node.Keys, index, node.Keys, index + 1, node.Count - index);
        node.Count++;
    }

    // Removes the child at `index` (and the separator in front of it).
    private static void ShiftLeft(Internal node, int index)
    {
        node.Count--;
        Array.Copy(node.Children, index + 1, node.Children, index, node.Count - index);
        Array.Copy(node.ChildSizes, index + 1, node.ChildSizes, index, node.Count - index);
        Array.Copy(node.Keys, index + 1, node.Keys, index, node.Count - index);
        ClearSlot(node, node.Count);
        node.Keys[0] = default!;
    }

    private static void ClearSlot(Internal node, int index)
    {
        node.Children[index] = null!;
        node.ChildSizes[index] = 0;
        node.Keys[index] = default!;
    }

    private static int Size(Node node)
    {
        if (node is Leaf leaf)
        {
            return leaf.Count;
        }

        var parent = (Internal)node;
        int size = 0;
        for (int i = 0; i < parent.Count; i++)
        {
            size += parent.ChildSizes[i];
        }

        return size;
    }

    private int ValidateNode(Node node, bool isRoot, int depth, ref int leafDepth, bool hasLow, TKey low, bool hasHigh, TKey high)
    {
        if (node is Leaf leaf)
        {
            if (leafDepth == -1)
            {
                leafDepth = depth;
            }

            Check(leafDepth == depth, "leaves at different depths");
            Check(isRoot || leaf.Count >= MinLeaf, "under-full leaf");
            for (int i = 0; i < leaf.Count; i++)
            {
                Check(i == 0 || Less(leaf.Keys[i - 1], leaf.Keys[i]), "leaf keys out of order");
                Check(!hasLow || !Less(leaf.Keys[i], low), "key below separator");
                Check(!hasHigh || Less(leaf.Keys[i], high), "key above separator");
            }

            return leaf.Count;
        }

        var parent = (Internal)node;
        Check(parent.Count >= (isRoot ? 2 : MinInternal), "under-full internal node");
        int total = 0;
        for (int i = 0; i < parent.Count; i++)
        {
            bool childHasLow = i > 0 || hasLow;
            TKey childLow = i > 0 ? parent.Keys[i] : low;
            bool childHasHigh = i + 1 < parent.Count || hasHigh;
            TKey childHigh = i + 1 < parent.Count ? parent.Keys[i + 1] : high;
            int size = ValidateNode(parent.Children[i], false, depth + 1, ref leafDepth, childHasLow, childLow, childHasHigh, childHigh);
            Check(size == parent.ChildSizes[i], "wrong child size");
            total += size;
        }

        return total;

        static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("B-tree invariant violated: " + message);
            }
        }
    }

    [DoesNotReturn]
    private static KeyValuePair<TKey, TValue> ThrowEmpty()
    {
        ThrowHelper.ThrowEmptyCollection("dictionary");
        return default;
    }

    private abstract class Node
    {
        // Leaf: number of entries. Internal: number of children.
        public int Count;
    }

    private sealed class Leaf : Node
    {
        public readonly TKey[] Keys = new TKey[LeafCapacity];
        public readonly TValue[] Values = new TValue[LeafCapacity];
        public Leaf? Next;
        public Leaf? Previous;
    }

    private sealed class Internal : Node
    {
        // Keys[i] (i >= 1) is the smallest key in Children[i]; Keys[0] is unused.
        public readonly TKey[] Keys = new TKey[InternalCapacity];
        public readonly Node[] Children = new Node[InternalCapacity];
        public readonly int[] ChildSizes = new int[InternalCapacity];
    }

    /// <summary>Entries with keys in a closed range. See <see cref="EnumerateRange"/>.</summary>
    public readonly struct RangeEnumerable : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly BTreeDictionary<TKey, TValue> _tree;
        private readonly object? _leaf;
        private readonly int _index;
        private readonly TKey _to;
        private readonly bool _bounded;

        internal RangeEnumerable(BTreeDictionary<TKey, TValue> tree, object? leaf, int index, TKey to, bool bounded)
        {
            _tree = tree;
            _leaf = leaf;
            _index = index;
            _to = to;
            _bounded = bounded;
        }

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public Enumerator GetEnumerator() => new(_tree, (Leaf?)_leaf, _index, _to, _bounded);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Walks the linked leaves in key order. Throws if the dictionary is modified during enumeration.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly BTreeDictionary<TKey, TValue> _tree;
        private readonly int _version;
        private readonly object? _startLeaf;
        private readonly int _startIndex;
        private readonly TKey _to;
        private readonly bool _bounded;
        private object? _leaf;
        private int _index;
        private KeyValuePair<TKey, TValue> _current;

        internal Enumerator(BTreeDictionary<TKey, TValue> tree, object? leaf, int index, TKey to, bool bounded)
        {
            _tree = tree;
            _version = tree._version;
            _startLeaf = leaf;
            _startIndex = index;
            _to = to;
            _bounded = bounded;
            _leaf = leaf;
            _index = index - 1;
            _current = default;
        }

        /// <inheritdoc />
        public readonly KeyValuePair<TKey, TValue> Current => _current;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _tree._version)
            {
                ThrowHelper.ThrowCollectionModified();
            }

            var leaf = (Leaf?)_leaf;
            if (leaf is null)
            {
                return false;
            }

            _index++;
            while (_index >= leaf.Count)
            {
                leaf = leaf.Next;
                _leaf = leaf;
                _index = 0;
                if (leaf is null)
                {
                    return false;
                }
            }

            TKey key = leaf.Keys[_index];
            if (_bounded && _tree.Less(_to, key))
            {
                _leaf = null;
                return false;
            }

            _current = new(key, leaf.Values[_index]);
            return true;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _leaf = _startLeaf;
            _index = _startIndex - 1;
            _current = default;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
