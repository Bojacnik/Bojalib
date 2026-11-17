using System.Buffers;
using System.Collections;

namespace Bojalib.Collections;

public sealed partial class Trie<TValue>
{
    /// <summary>
    /// Enumerates the keys that match <paramref name="pattern"/>, where <paramref name="wildcard"/> matches any single
    /// character (e.g. <c>"c?t"</c> matches <c>"cat"</c> and <c>"cut"</c>). Only branches that can still match are
    /// visited.
    /// </summary>
    /// <param name="pattern">The pattern; matching keys have the same length.</param>
    /// <param name="wildcard">The character that matches anything.</param>
    /// <returns>The matching key/value pairs, in key order. Enumeration is lazy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public IEnumerable<KeyValuePair<string, TValue>> EnumerateMatches(string pattern, char wildcard = '?')
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return Matches(FoldPattern(pattern, wildcard), wildcard);
    }

    /// <summary>
    /// Enumerates the keys within <paramref name="maxDistance"/> edits (insertions, deletions or substitutions —
    /// Levenshtein distance) of <paramref name="word"/>: spelling suggestions, fuzzy lookup. Each trie node computes
    /// one row of the edit-distance table from its parent's row, and branches that are already too far away are
    /// skipped, so this is much faster than comparing against every key.
    /// </summary>
    /// <param name="word">The word to compare against.</param>
    /// <param name="maxDistance">The largest edit distance to include.</param>
    /// <returns>The matching keys with their values and distances, in key order. Enumeration is lazy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="word"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> is negative.</exception>
    public IEnumerable<FuzzyMatch> EnumerateFuzzy(string word, int maxDistance)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        return Fuzzy(IgnoreCase ? Fold(word) : word, maxDistance);
    }

    private string FoldPattern(string pattern, char wildcard)
    {
        if (!IgnoreCase)
        {
            return pattern;
        }

        return string.Create(pattern.Length, (pattern, wildcard), static (span, state) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = state.pattern[i];
                span[i] = c == state.wildcard ? c : char.ToUpperInvariant(c);
            }
        });
    }

    private IEnumerable<KeyValuePair<string, TValue>> Matches(string pattern, char wildcard)
    {
        int version = _version;
        var pending = new Stack<(int Node, int Position)>();
        pending.Push((Root, 0));

        while (pending.TryPop(out (int Node, int Position) item))
        {
            if (item.Position == pattern.Length)
            {
                // Children would only make longer keys.
                if (_nodes[item.Node].Key is { } key)
                {
                    yield return new(key, _nodes[item.Node].Value);
                    CheckVersion(version);
                }

                continue;
            }

            // Push in reverse so children come out in key order.
            for (int s = _nodes[item.Node].ChildCount - 1; s >= 0; s--)
            {
                int child = _nodes[item.Node].Children![s];
                ReadOnlySpan<char> label = Label(child);
                if (item.Position + label.Length <= pattern.Length && Matches(label, pattern.AsSpan(item.Position, label.Length), wildcard))
                {
                    pending.Push((child, item.Position + label.Length));
                }
            }
        }

        static bool Matches(ReadOnlySpan<char> label, ReadOnlySpan<char> pattern, char wildcard)
        {
            for (int i = 0; i < label.Length; i++)
            {
                if (pattern[i] != wildcard && pattern[i] != label[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    private IEnumerable<FuzzyMatch> Fuzzy(string word, int maxDistance)
    {
        int version = _version;
        int width = word.Length + 1;

        // rows[d] is the edit-distance row after d characters of the current path. Depth-first order means a
        // node's parent row is still intact when the node is visited, so one buffer serves the whole search.
        int[] rows = ArrayPool<int>.Shared.Rent(width * 16);
        try
        {
            for (int j = 0; j < width; j++)
            {
                rows[j] = j;
            }

            if (_nodes[Root].Key is { } rootKey && word.Length <= maxDistance)
            {
                yield return new(rootKey, _nodes[Root].Value, word.Length);
                CheckVersion(version);
            }

            var pending = new Stack<(int Node, int Depth)>();
            PushChildren(pending, Root, 0);

            while (pending.TryPop(out (int Node, int Depth) item))
            {
                ReadOnlySpan<char> label = Label(item.Node);
                int depth = item.Depth;
                int rowMinimum = 0;

                foreach (char c in label)
                {
                    if ((depth + 2) * width > rows.Length)
                    {
                        int[] bigger = ArrayPool<int>.Shared.Rent(rows.Length * 2);
                        rows.AsSpan(0, (depth + 1) * width).CopyTo(bigger);
                        ArrayPool<int>.Shared.Return(rows);
                        rows = bigger;
                    }

                    rowMinimum = NextRow(rows.AsSpan(depth * width, width), rows.AsSpan((depth + 1) * width, width), c, word);
                    depth++;
                    if (rowMinimum > maxDistance)
                    {
                        break; // Every key below is at least this far away.
                    }
                }

                if (rowMinimum > maxDistance)
                {
                    continue;
                }

                int distance = rows[(depth * width) + word.Length];
                if (_nodes[item.Node].Key is { } key && distance <= maxDistance)
                {
                    yield return new(key, _nodes[item.Node].Value, distance);
                    CheckVersion(version);
                }

                PushChildren(pending, item.Node, depth);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rows);
        }

        void PushChildren(Stack<(int Node, int Depth)> pending, int node, int depth)
        {
            for (int s = _nodes[node].ChildCount - 1; s >= 0; s--)
            {
                pending.Push((_nodes[node].Children![s], depth));
            }
        }

        // One Levenshtein row: distances between `word` prefixes and the path extended by `c`. Returns its minimum.
        static int NextRow(ReadOnlySpan<int> previous, Span<int> next, char c, string word)
        {
            next[0] = previous[0] + 1;
            int minimum = next[0];
            for (int j = 1; j < next.Length; j++)
            {
                int substitute = previous[j - 1] + (word[j - 1] == c ? 0 : 1);
                int value = Math.Min(Math.Min(previous[j] + 1, next[j - 1] + 1), substitute);
                next[j] = value;
                minimum = Math.Min(minimum, value);
            }

            return minimum;
        }
    }

    private void CheckVersion(int version)
    {
        if (version != _version)
        {
            ThrowHelper.ThrowCollectionModified();
        }
    }

    /// <summary>A key found by <see cref="EnumerateFuzzy"/>, with its value and edit distance from the search word.</summary>
    /// <param name="Key">The matching key.</param>
    /// <param name="Value">Its value.</param>
    /// <param name="Distance">The Levenshtein distance between the key and the search word.</param>
    public readonly record struct FuzzyMatch(string Key, TValue Value, int Distance);

    /// <summary>The keys with a given prefix. See <see cref="EnumeratePrefix"/>.</summary>
    public readonly struct PrefixEnumerable : IEnumerable<KeyValuePair<string, TValue>>
    {
        private readonly Trie<TValue> _trie;
        private readonly int _start;

        internal PrefixEnumerable(Trie<TValue> trie, int start)
        {
            _trie = trie;
            _start = start;
        }

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public Enumerator GetEnumerator() => new(_trie, _start);

        IEnumerator<KeyValuePair<string, TValue>> IEnumerable<KeyValuePair<string, TValue>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Enumerates key/value pairs in key order by walking parent links — no stack, no allocation. Throws if the trie
    /// is modified during enumeration.
    /// </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, TValue>>
    {
        private readonly Trie<TValue> _trie;
        private readonly int _start;
        private readonly int _version;
        private int _node;
        private bool _started;
        private KeyValuePair<string, TValue> _current;

        internal Enumerator(Trie<TValue> trie, int start)
        {
            _trie = trie;
            _start = start;
            _version = trie._version;
            _node = -1;
            _started = false;
            _current = default;
        }

        /// <inheritdoc />
        public readonly KeyValuePair<string, TValue> Current => _current;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            _trie.CheckVersion(_version);
            if (_start < 0)
            {
                return false;
            }

            int node = _started ? (_node < 0 ? -1 : _trie.Next(_node, _start)) : _start;
            _started = true;
            while (node >= 0)
            {
                ref Node n = ref _trie._nodes[node];
                if (n.Key is not null)
                {
                    _node = node;
                    _current = new(n.Key, n.Value);
                    return true;
                }

                node = _trie.Next(node, _start);
            }

            _node = -1;
            _current = default;
            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _trie.CheckVersion(_version);
            _node = -1;
            _started = false;
            _current = default;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
