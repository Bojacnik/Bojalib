using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Bojalib.Collections.Internal;

namespace Bojalib.Collections;

/// <summary>
/// Prefix tree mapping string keys to values. Lookups cost O(key length) regardless of how many keys are stored;
/// all keys sharing a prefix can be enumerated without scanning the rest; and it supports longest-prefix matching,
/// wildcard and fuzzy (edit-distance) search.
/// </summary>
/// <remarks>
/// <para>
/// Implemented as a radix tree: chains of single-child nodes are merged into one edge labelled with several
/// characters, so a lookup visits few nodes. Nodes are structs in one array and labels live in one shared character
/// buffer, so the whole trie is a handful of objects. Child selection and label comparison use the runtime's
/// vectorized span search.
/// </para>
/// <para>
/// Exact-key operations (<see cref="TryGetValue(ReadOnlySpan{char}, out TValue)"/>, <see cref="ContainsKey(ReadOnlySpan{char})"/>,
/// the indexer, <see cref="Remove(string)"/>) go through a hash index of the stored keys, so they run at
/// <see cref="Dictionary{TKey, TValue}"/> speed; the tree serves prefix, longest-match, wildcard and fuzzy queries.
/// The index costs roughly one dictionary entry per key (the key strings themselves are shared).
/// </para>
/// <para>
/// Keys are compared ordinally (or ordinal-ignore-case) and enumerated in that order. Lookups take
/// <see cref="ReadOnlySpan{T}"/>, so slices of larger strings need no allocation. Enumerating keys returns the
/// strings as they were added, without allocating.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The value type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed partial class Trie<TValue> : IReadOnlyDictionary<string, TValue>
{
    private const int Root = 0;
    private const int FreeMarker = -2;
    private const int StackFoldLimit = 256;

    private Node[] _nodes = new Node[16];
    private int _nodeCount = 1; // Index 0 is the root, which has an empty label.
    private int _freeHead = -1;

    private char[] _labels = new char[64];
    private int _labelLength;
    private int _liveLabelChars;

    private int _count;
    private int _version;

    // Key string -> node holding it, for exact lookups at hash-table speed.
    private readonly Dictionary<string, int> _index;
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    /// <summary>Creates an empty trie.</summary>
    /// <param name="ignoreCase">
    /// <see langword="true"/> to treat keys that differ only in case as the same key. Each UTF-16 unit is folded with
    /// <see cref="char.ToUpperInvariant"/> (so letters outside the Basic Multilingual Plane, stored as surrogate pairs,
    /// compare exactly). Enumerated keys keep the casing they were first added with.
    /// </param>
    public Trie(bool ignoreCase = false)
    {
        IgnoreCase = ignoreCase;
        _nodes[Root].Parent = -1;
        _index = new Dictionary<string, int>(ignoreCase ? FoldingComparer.Instance : StringComparer.Ordinal);
        _lookup = _index.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Gets whether keys are compared ignoring case.</summary>
    public bool IgnoreCase { get; }

    /// <summary>Gets the number of keys in the trie.</summary>
    public int Count => _count;

    /// <summary>Gets whether the trie has no keys.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Gets all keys, in key order.</summary>
    public IEnumerable<string> Keys => this.Select(static pair => pair.Key);

    /// <summary>Gets all values, in the order of their keys.</summary>
    public IEnumerable<TValue> Values => this.Select(static pair => pair.Value);

    /// <summary>Gets or sets the value for <paramref name="key"/>. Setting adds the key or replaces its value.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">Getting a key that isn't in the trie.</exception>
    public TValue this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return _index.TryGetValue(key, out int node)
                ? _nodes[node].Value
                : throw new KeyNotFoundException($"The key '{key}' was not found.");
        }

        set
        {
            ArgumentNullException.ThrowIfNull(key);
            Insert(key, key, value, overwrite: true);
        }
    }

    /// <summary>
    /// Gets or sets the value for <paramref name="key"/>. Setting adds the key or replaces its value; adding a new
    /// key from a span allocates its string.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <exception cref="KeyNotFoundException">Getting a key that isn't in the trie.</exception>
    public TValue this[ReadOnlySpan<char> key]
    {
        get
        {
            if (!_lookup.TryGetValue(key, out int node))
            {
                throw new KeyNotFoundException($"The key '{key}' was not found.");
            }

            return _nodes[node].Value;
        }

        set => Insert(key, null, value, overwrite: true);
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The key is already in the trie.</exception>
    public void Add(string key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Insert(key, key, value, overwrite: false))
        {
            throw new ArgumentException($"An item with the key '{key}' has already been added.", nameof(key));
        }
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The key is already in the trie.</exception>
    public void Add(ReadOnlySpan<char> key, TValue value)
    {
        if (!Insert(key, null, value, overwrite: false))
        {
            throw new ArgumentException($"An item with the key '{key}' has already been added.", nameof(key));
        }
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/> unless the key is already present.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the key was added; <see langword="false"/> if it already existed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryAdd(string key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Insert(key, key, value, overwrite: false);
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/> unless the key is already present.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the key was added; <see langword="false"/> if it already existed.</returns>
    public bool TryAdd(ReadOnlySpan<char> key, TValue value) => Insert(key, null, value, overwrite: false);

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, or <c>default</c> if the key isn't present.</param>
    /// <returns>
    /// <see langword="true"/> if the key is present. A key that is only a prefix of stored keys is not present.
    /// </returns>
    public bool TryGetValue(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_lookup.TryGetValue(key, out int node))
        {
            value = _nodes[node].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the value for <paramref name="key"/>. Faster than the span overload for whole strings.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, or <c>default</c> if the key isn't present.</param>
    /// <returns><see langword="true"/> if the key is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_index.TryGetValue(key, out int node))
        {
            value = _nodes[node].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Returns whether <paramref name="key"/> is in the trie.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if the key is present.</returns>
    public bool ContainsKey(ReadOnlySpan<char> key) => _lookup.ContainsKey(key);

    /// <summary>Returns whether <paramref name="key"/> is in the trie. Faster than the span overload for whole strings.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if the key is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool ContainsKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _index.ContainsKey(key);
    }

    /// <summary>Returns whether any key starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The prefix. The empty prefix matches if the trie is not empty.</param>
    /// <returns><see langword="true"/> if at least one key starts with the prefix.</returns>
    public bool ContainsPrefix(ReadOnlySpan<char> prefix) => _count > 0 && FindPrefix(prefix) >= 0;

    /// <summary>
    /// Finds the longest key that is a prefix of <paramref name="text"/> — the core operation of tokenizers,
    /// routers and longest-match dictionaries. The text is not copied or case-folded up front, so it can be the
    /// whole rest of a large document.
    /// </summary>
    /// <param name="text">The text to match at its start.</param>
    /// <param name="length">The length of the matched key.</param>
    /// <param name="value">The matched key's value.</param>
    /// <returns><see langword="true"/> if some key (possibly the empty key) is a prefix of the text.</returns>
    public bool TryMatchLongestPrefix(ReadOnlySpan<char> text, out int length, [MaybeNullWhen(false)] out TValue value)
    {
        int best = _nodes[Root].Key is not null ? Root : -1;
        int bestLength = 0;
        int node = Root;
        int i = 0;

        while (i < text.Length)
        {
            int slot = FindChildSlot(node, Fold(text[i]));
            if (slot < 0)
            {
                break;
            }

            int child = _nodes[node].Children![slot];
            ReadOnlySpan<char> label = Label(child);
            if (text.Length - i < label.Length || !LabelEquals(text.Slice(i, label.Length), label))
            {
                break;
            }

            i += label.Length;
            node = child;
            if (_nodes[node].Key is not null)
            {
                best = node;
                bestLength = i;
            }
        }

        if (best < 0)
        {
            length = 0;
            value = default;
            return false;
        }

        length = bestLength;
        value = _nodes[best].Value;
        return true;
    }

    /// <summary>Removes <paramref name="key"/>, merging nodes that are no longer needed.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if the key was removed; <see langword="false"/> if it wasn't present.</returns>
    public bool Remove(ReadOnlySpan<char> key) => _lookup.Remove(key, out _, out int node) && RemoveNode(node);

    /// <summary>Removes <paramref name="key"/>, merging nodes that are no longer needed.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if the key was removed; <see langword="false"/> if it wasn't present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _index.Remove(key, out int node) && RemoveNode(node);
    }

    // Clears the value at `node` (already removed from the index) and prunes or merges nodes.
    private bool RemoveNode(int node)
    {
        ref Node n = ref _nodes[node];
        n.Key = null;
        n.Value = default!;
        _count--;
        _version++;

        if (node == Root)
        {
            return true;
        }

        if (n.ChildCount == 0)
        {
            int parent = n.Parent;
            RemoveChildSlot(parent, n.Slot);
            FreeNode(node);
            if (parent != Root && _nodes[parent].Key is null && _nodes[parent].ChildCount == 1)
            {
                MergeWithOnlyChild(parent);
            }
        }
        else if (n.ChildCount == 1)
        {
            MergeWithOnlyChild(node);
        }

        CompactLabelsIfWasteful();
        return true;
    }

    /// <summary>Removes all keys.</summary>
    public void Clear()
    {
        Array.Clear(_nodes, 0, _nodeCount);
        _nodes[Root].Parent = -1;
        _nodeCount = 1;
        _freeHead = -1;
        _labelLength = 0;
        _liveLabelChars = 0;
        _count = 0;
        _index.Clear();
        _version++;
    }

    /// <summary>Enumerates the keys starting with <paramref name="prefix"/> (including the prefix itself), in key order.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>An allocation-free enumerable of the matching key/value pairs.</returns>
    public PrefixEnumerable EnumeratePrefix(ReadOnlySpan<char> prefix) => new(this, FindPrefix(prefix));

    /// <summary>Enumerates all key/value pairs in key order.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(this, Root);

    IEnumerator<KeyValuePair<string, TValue>> IEnumerable<KeyValuePair<string, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Fold(char c) => IgnoreCase ? char.ToUpperInvariant(c) : c;

    // The single definition of case folding used everywhere in the trie: char.ToUpperInvariant per UTF-16 unit.
    private static void FoldInto(ReadOnlySpan<char> source, Span<char> destination)
    {
        for (int i = 0; i < source.Length; i++)
        {
            destination[i] = char.ToUpperInvariant(source[i]);
        }
    }

    private static string Fold(string text) => string.Create(text.Length, text, static (span, t) => FoldInto(t, span));

    // Labels are stored folded, so only the text side needs folding when ignoring case. Folding is per UTF-16 unit,
    // exactly as on insert (OrdinalIgnoreCase would also fold surrogate pairs, and the two must agree).
    private bool LabelEquals(ReadOnlySpan<char> text, ReadOnlySpan<char> label)
    {
        if (text.SequenceEqual(label))
        {
            return true;
        }

        if (!IgnoreCase)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (char.ToUpperInvariant(text[i]) != label[i])
            {
                return false;
            }
        }

        return true;
    }

    private ReadOnlySpan<char> Label(int node) => _labels.AsSpan(_nodes[node].LabelStart, _nodes[node].LabelLength);

    private int FindChildSlot(int node, char c)
    {
        ref Node n = ref _nodes[node];
        return n.ChildCount == 0 ? -1 : n.ChildChars.AsSpan(0, n.ChildCount).IndexOf(c);
    }

    /// <summary>The topmost node whose subtree holds exactly the keys starting with <paramref name="prefix"/>, or -1.</summary>
    private int FindPrefix(ReadOnlySpan<char> prefix)
    {
        int node = Root;
        int i = 0;
        while (i < prefix.Length)
        {
            int slot = FindChildSlot(node, Fold(prefix[i]));
            if (slot < 0)
            {
                return -1;
            }

            int child = _nodes[node].Children![slot];
            ReadOnlySpan<char> label = Label(child);
            int remaining = prefix.Length - i;
            if (remaining < label.Length)
            {
                // The prefix ends inside this edge.
                return LabelEquals(prefix[i..], label[..remaining]) ? child : -1;
            }

            if (!LabelEquals(prefix.Slice(i, label.Length), label))
            {
                return -1;
            }

            i += label.Length;
            node = child;
        }

        return node;
    }

    private bool Insert(ReadOnlySpan<char> key, string? keyString, TValue value, bool overwrite)
    {
        // Uninitialized ([SkipLocalsInit]), so this costs nothing when unused.
        Span<char> stackBuffer = stackalloc char[StackFoldLimit];
        char[]? rented = null;
        scoped ReadOnlySpan<char> folded = key;
        if (IgnoreCase)
        {
            Span<char> buffer = key.Length <= StackFoldLimit
                ? stackBuffer
                : (rented = ArrayPool<char>.Shared.Rent(key.Length));
            buffer = buffer[..key.Length];
            FoldInto(key, buffer);
            folded = buffer;
        }

        try
        {
            int node = FindOrCreateNode(folded);
            ref Node n = ref _nodes[node];
            if (n.Key is not null)
            {
                if (!overwrite)
                {
                    return false;
                }
            }
            else
            {
                n.Key = keyString ?? key.ToString();
                _index.Add(n.Key, node);
                _count++;
            }

            n.Value = value;
            _version++;
            return true;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private int FindOrCreateNode(ReadOnlySpan<char> folded)
    {
        int node = Root;
        int i = 0;
        while (i < folded.Length)
        {
            int slot = FindChildSlot(node, folded[i]);
            if (slot < 0)
            {
                int leaf = AllocateNode();
                int labelStart = AppendLabel(folded[i..]);
                _nodes[leaf].LabelStart = labelStart;
                _nodes[leaf].LabelLength = folded.Length - i;
                _liveLabelChars += folded.Length - i;
                InsertChild(node, leaf, folded[i]);
                return leaf;
            }

            int child = _nodes[node].Children![slot];
            ReadOnlySpan<char> label = Label(child);
            int common = folded[i..].CommonPrefixLength(label);
            if (common == label.Length)
            {
                i += common;
                node = child;
                continue;
            }

            // Split the edge: a new middle node takes the shared part of the label.
            int middle = AllocateNode();
            ref Node m = ref _nodes[middle];
            ref Node c = ref _nodes[child];
            m.LabelStart = c.LabelStart;
            m.LabelLength = common;
            c.LabelStart += common;
            c.LabelLength -= common;

            _nodes[node].Children![slot] = middle;
            m.Parent = node;
            m.Slot = slot;
            m.ChildChars = [_labels[c.LabelStart], default];
            m.Children = [child, 0];
            m.ChildCount = 1;
            c.Parent = middle;
            c.Slot = 0;

            i += common;
            node = middle;
        }

        return node;
    }

    private int AllocateNode()
    {
        if (_freeHead >= 0)
        {
            int free = _freeHead;
            _freeHead = _nodes[free].Slot;
            _nodes[free] = default;
            return free;
        }

        if (_nodeCount == _nodes.Length)
        {
            Array.Resize(ref _nodes, _nodes.Length * 2);
        }

        return _nodeCount++;
    }

    private void FreeNode(int node)
    {
        _liveLabelChars -= _nodes[node].LabelLength;
        _nodes[node] = default;
        _nodes[node].Parent = FreeMarker;
        _nodes[node].Slot = _freeHead;
        _freeHead = node;
    }

    private int AppendLabel(ReadOnlySpan<char> chars)
    {
        EnsureLabelCapacity(chars.Length);
        int start = _labelLength;
        chars.CopyTo(_labels.AsSpan(start));
        _labelLength += chars.Length;
        return start;
    }

    private void EnsureLabelCapacity(int extra)
    {
        if (_labelLength + extra > _labels.Length)
        {
            Array.Resize(ref _labels, Math.Max(_labels.Length * 2, _labelLength + extra));
        }
    }

    private void InsertChild(int parent, int child, char first)
    {
        ref Node p = ref _nodes[parent];
        if (p.ChildChars is null)
        {
            p.ChildChars = new char[2];
            p.Children = new int[2];
        }
        else if (p.ChildCount == p.ChildChars.Length)
        {
            Array.Resize(ref p.ChildChars, p.ChildCount * 2);
            Array.Resize(ref p.Children, p.ChildCount * 2);
        }

        int position = ~p.ChildChars.AsSpan(0, p.ChildCount).BinarySearch(first);
        Array.Copy(p.ChildChars, position, p.ChildChars, position + 1, p.ChildCount - position);
        Array.Copy(p.Children!, position, p.Children!, position + 1, p.ChildCount - position);
        p.ChildChars[position] = first;
        p.Children![position] = child;
        p.ChildCount++;

        for (int s = position; s < p.ChildCount; s++)
        {
            ref Node sibling = ref _nodes[p.Children[s]];
            sibling.Parent = parent;
            sibling.Slot = s;
        }
    }

    private void RemoveChildSlot(int parent, int slot)
    {
        ref Node p = ref _nodes[parent];
        p.ChildCount--;
        Array.Copy(p.ChildChars!, slot + 1, p.ChildChars!, slot, p.ChildCount - slot);
        Array.Copy(p.Children!, slot + 1, p.Children!, slot, p.ChildCount - slot);
        for (int s = slot; s < p.ChildCount; s++)
        {
            _nodes[p.Children![s]].Slot = s;
        }
    }

    /// <summary>Folds a valueless node's only child into it, concatenating their labels.</summary>
    private void MergeWithOnlyChild(int node)
    {
        int child = _nodes[node].Children![0];
        int nodeLength = _nodes[node].LabelLength;
        int childLength = _nodes[child].LabelLength;

        EnsureLabelCapacity(nodeLength + childLength);
        int start = _labelLength;
        Array.Copy(_labels, _nodes[node].LabelStart, _labels, start, nodeLength);
        Array.Copy(_labels, _nodes[child].LabelStart, _labels, start + nodeLength, childLength);
        _labelLength += nodeLength + childLength;

        ref Node n = ref _nodes[node];
        ref Node c = ref _nodes[child];
        n.LabelStart = start;
        n.LabelLength = nodeLength + childLength;
        n.Key = c.Key;
        n.Value = c.Value;
        if (n.Key is not null)
        {
            _index[n.Key] = node; // the key moved up into this node
        }
        n.ChildChars = c.ChildChars;
        n.Children = c.Children;
        n.ChildCount = c.ChildCount;
        for (int s = 0; s < n.ChildCount; s++)
        {
            _nodes[n.Children![s]].Parent = node;
        }

        // The child's characters now live in the node's new label: don't count them as freed.
        c.LabelLength = 0;
        FreeNode(child);
    }

    // Labels are append-only; once more than half the buffer is dead, rewrite it with only the live labels.
    private void CompactLabelsIfWasteful()
    {
        if (_labelLength <= 64 || _labelLength <= 2 * _liveLabelChars)
        {
            return;
        }

        char[] compacted = new char[Math.Max(64, _liveLabelChars * 2)];
        int position = 0;
        for (int i = 1; i < _nodeCount; i++)
        {
            ref Node n = ref _nodes[i];
            if (n.Parent == FreeMarker)
            {
                continue;
            }

            Array.Copy(_labels, n.LabelStart, compacted, position, n.LabelLength);
            n.LabelStart = position;
            position += n.LabelLength;
        }

        _labels = compacted;
        _labelLength = position;
    }

    /// <summary>The node after <paramref name="node"/> in pre-order within <paramref name="start"/>'s subtree, or -1.</summary>
    private int Next(int node, int start)
    {
        if (_nodes[node].ChildCount > 0)
        {
            return _nodes[node].Children![0];
        }

        while (node != start)
        {
            ref Node n = ref _nodes[node];
            ref Node parent = ref _nodes[n.Parent];
            if (n.Slot + 1 < parent.ChildCount)
            {
                return parent.Children![n.Slot + 1];
            }

            node = n.Parent;
        }

        return -1;
    }

    private struct Node
    {
        // Non-null exactly when this node holds a value; also what enumeration returns, so no key is rebuilt.
        public string? Key;
        public TValue Value;

        public int Parent; // -1 for the root, FreeMarker for free nodes.
        public int Slot;   // Index in the parent's child arrays (next free node, for free nodes).

        // The edge into this node: _labels[LabelStart .. LabelStart + LabelLength), case-folded if IgnoreCase.
        public int LabelStart;
        public int LabelLength;

        // First label character of each child, sorted, with the child indices alongside.
        public char[]? ChildChars;
        public int[]? Children;
        public int ChildCount;
    }
}
