using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Bojalib.Collections;

/// <summary>
/// Prefix tree mapping string keys to values. Lookups cost O(key length) regardless of how many keys are stored,
/// and all keys sharing a prefix can be enumerated without scanning the rest.
/// </summary>
/// <remarks>
/// Keys are compared ordinally (char by char, case-sensitive) and enumerated in ordinal order.
/// Lookup methods take <see cref="ReadOnlySpan{T}"/> so slices of larger strings can be looked up without allocating.
/// </remarks>
/// <typeparam name="TValue">The value type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "'Trie' is the established name of this data structure.")]
public sealed class Trie<TValue> : IReadOnlyDictionary<string, TValue>
{
    private readonly Node _root = new();
    private int _count;
    private int _version;

    /// <summary>Gets the number of keys in the trie.</summary>
    public int Count => _count;

    /// <summary>Gets whether the trie has no keys.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Gets all keys, in ordinal order.</summary>
    public IEnumerable<string> Keys => this.Select(static pair => pair.Key);

    /// <summary>Gets all values, in the ordinal order of their keys.</summary>
    public IEnumerable<TValue> Values => this.Select(static pair => pair.Value);

    /// <summary>Gets or sets the value for <paramref name="key"/>. Setting adds the key or replaces its value.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="KeyNotFoundException">Getting a key that isn't in the trie.</exception>
    public TValue this[ReadOnlySpan<char> key]
    {
        get
        {
            Node? node = FindNode(key);
            if (node is not { HasValue: true })
            {
                throw new KeyNotFoundException($"The key '{key}' was not found.");
            }

            return node.Value;
        }
        set => Insert(key, value, overwrite: true);
    }

    TValue IReadOnlyDictionary<string, TValue>.this[string key] => this[key];

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The key is already in the trie.</exception>
    public void Add(ReadOnlySpan<char> key, TValue value)
    {
        if (!Insert(key, value, overwrite: false))
        {
            throw new ArgumentException($"An item with the key '{key}' has already been added.", nameof(key));
        }
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/> unless the key is already present.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the key was added; <see langword="false"/> if it already existed.</returns>
    public bool TryAdd(ReadOnlySpan<char> key, TValue value) => Insert(key, value, overwrite: false);

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, or <c>default</c> if the key isn't present.</param>
    /// <returns>
    /// <see langword="true"/> if the key is present. A key that is only a prefix of stored keys is not present.
    /// </returns>
    public bool TryGetValue(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out TValue value)
    {
        Node? node = FindNode(key);
        if (node is { HasValue: true })
        {
            value = node.Value;
            return true;
        }

        value = default;
        return false;
    }

    bool IReadOnlyDictionary<string, TValue>.TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryGetValue(key.AsSpan(), out value);
    }

    /// <summary>Returns whether <paramref name="key"/> is in the trie.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if the key is present.</returns>
    public bool ContainsKey(ReadOnlySpan<char> key) => FindNode(key) is { HasValue: true };

    bool IReadOnlyDictionary<string, TValue>.ContainsKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ContainsKey(key.AsSpan());
    }

    /// <summary>Returns whether any key starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The prefix. The empty prefix matches if the trie is not empty.</param>
    /// <returns><see langword="true"/> if at least one key starts with the prefix.</returns>
    public bool ContainsPrefix(ReadOnlySpan<char> prefix)
    {
        Node? node = FindNode(prefix);
        return node is not null && (node.HasValue || node.ChildCount > 0);
    }

    /// <summary>Removes <paramref name="key"/> and frees nodes no longer used by other keys.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if the key was removed; <see langword="false"/> if it wasn't present.</returns>
    public bool Remove(ReadOnlySpan<char> key)
    {
        // Remember the deepest node that must survive (it has a value or another branch); everything below
        // it on this path exists only for the removed key and can be unlinked in one step.
        Node node = _root;
        Node keepNode = _root;
        int keepIndex = 0;

        for (int i = 0; i < key.Length; i++)
        {
            if (node.HasValue || node.ChildCount > 1)
            {
                keepNode = node;
                keepIndex = i;
            }

            Node? child = node.GetChild(key[i]);
            if (child is null)
            {
                return false;
            }

            node = child;
        }

        if (!node.HasValue)
        {
            return false;
        }

        if (node.ChildCount > 0 || key.IsEmpty)
        {
            node.HasValue = false;
            node.Value = default!;
        }
        else
        {
            keepNode.RemoveChild(key[keepIndex]);
        }

        _count--;
        _version++;
        return true;
    }

    /// <summary>Removes all keys.</summary>
    public void Clear()
    {
        _root.Clear();
        _count = 0;
        _version++;
    }

    /// <summary>Enumerates the keys starting with <paramref name="prefix"/> (including the prefix itself), in ordinal order.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The matching key/value pairs. Enumeration is lazy.</returns>
    public IEnumerable<KeyValuePair<string, TValue>> EnumeratePrefix(ReadOnlySpan<char> prefix)
    {
        Node? node = FindNode(prefix);
        return node is null ? [] : Enumerate(node, prefix.ToString());
    }

    /// <summary>Enumerates all key/value pairs in ordinal key order.</summary>
    /// <returns>An enumerator over the pairs.</returns>
    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => Enumerate(_root, string.Empty).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool Insert(ReadOnlySpan<char> key, TValue value, bool overwrite)
    {
        Node node = _root;
        foreach (char c in key)
        {
            node = node.GetOrAddChild(c);
        }

        if (node.HasValue)
        {
            if (!overwrite)
            {
                return false;
            }
        }
        else
        {
            node.HasValue = true;
            _count++;
        }

        node.Value = value;
        _version++;
        return true;
    }

    private Node? FindNode(ReadOnlySpan<char> key)
    {
        Node? node = _root;
        for (int i = 0; i < key.Length && node is not null; i++)
        {
            node = node.GetChild(key[i]);
        }

        return node;
    }

    private IEnumerable<KeyValuePair<string, TValue>> Enumerate(Node start, string prefix)
    {
        int version = _version;
        if (start.HasValue)
        {
            yield return new(prefix, start.Value);
            if (version != _version)
            {
                ThrowHelper.ThrowCollectionModified();
            }
        }

        // Iterative pre-order walk; the key is built up in one buffer instead of allocating per level.
        var key = new StringBuilder(prefix);
        var frames = new Stack<(Node Node, int NextChild)>();
        frames.Push((start, 0));

        while (frames.TryPop(out (Node Node, int NextChild) frame))
        {
            if (frame.NextChild < frame.Node.ChildCount)
            {
                frames.Push((frame.Node, frame.NextChild + 1));
                (char c, Node child) = frame.Node.Children![frame.NextChild];
                key.Append(c);

                if (child.HasValue)
                {
                    yield return new(key.ToString(), child.Value);
                    if (version != _version)
                    {
                        ThrowHelper.ThrowCollectionModified();
                    }
                }

                frames.Push((child, 0));
            }
            else if (frames.Count > 0)
            {
                // Finished a child subtree: drop its character.
                key.Length--;
            }
        }
    }

    /// <summary>
    /// A trie node. Children are stored inline as (char, node) pairs in one array sorted by character: one memory
    /// read per level, enumeration is naturally in order, and leaf nodes allocate no child storage at all.
    /// </summary>
    private sealed class Node
    {
        // Below this many children a linear scan beats binary search.
        private const int LinearSearchLimit = 8;

        public TValue Value = default!;
        public bool HasValue;
        public Child[]? Children;
        public int ChildCount;

        public Node? GetChild(char c)
        {
            Child[]? children = Children;
            if (children is null)
            {
                return null;
            }

            int count = ChildCount;
            if (count <= LinearSearchLimit)
            {
                for (int i = 0; i < count; i++)
                {
                    if (children[i].Key == c)
                    {
                        return children[i].Node;
                    }
                }

                return null;
            }

            int index = IndexOf(children, count, c);
            return index >= 0 ? children[index].Node : null;
        }

        public Node GetOrAddChild(char c)
        {
            int index = Children is null ? ~0 : IndexOf(Children, ChildCount, c);
            if (index >= 0)
            {
                return Children![index].Node;
            }

            index = ~index;
            if (Children is null || ChildCount == Children.Length)
            {
                Array.Resize(ref Children, Children is null ? 1 : Children.Length * 2);
            }

            Array.Copy(Children, index, Children, index + 1, ChildCount - index);
            var node = new Node();
            Children[index] = new Child(c, node);
            ChildCount++;
            return node;
        }

        public void RemoveChild(char c)
        {
            int index = IndexOf(Children!, ChildCount, c);
            Debug.Assert(index >= 0, "RemoveChild is only called for existing children.");

            ChildCount--;
            Array.Copy(Children!, index + 1, Children!, index, ChildCount - index);
            Children![ChildCount] = default;
        }

        public void Clear()
        {
            Value = default!;
            HasValue = false;
            Children = null;
            ChildCount = 0;
        }

        /// <summary>Binary search by key; returns the index, or the bitwise complement of the insertion point.</summary>
        private static int IndexOf(Child[] children, int count, char c)
        {
            int low = 0;
            int high = count - 1;
            while (low <= high)
            {
                int middle = (int)((uint)(low + high) >> 1);
                char key = children[middle].Key;
                if (key == c)
                {
                    return middle;
                }

                if (key < c)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return ~low;
        }
    }

    private readonly record struct Child(char Key, Node Node);
}
