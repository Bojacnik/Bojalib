using System.Diagnostics.CodeAnalysis;

namespace Bojalib.Collections;

/// <summary>Prefix tree mapping string keys to values.</summary>
public sealed class TrieTree<TData>
{
    private readonly TrieNode _root = new();

    /// <summary>Stores <paramref name="data"/> under <paramref name="word"/>, replacing any existing value.</summary>
    public void InsertWord(string word, TData data)
    {
        ArgumentNullException.ThrowIfNull(word);

        TrieNode current = _root;
        foreach (char letter in word)
        {
            if (!current.Children.TryGetValue(letter, out TrieNode? child))
            {
                child = new TrieNode();
                current.Children.Add(letter, child);
            }

            current = child;
        }

        current.Data = data;
        current.HasData = true;
    }

    /// <summary>
    /// Gets the value stored under <paramref name="word"/>. Returns <see langword="false"/> if the word was never
    /// inserted, including when it is only a prefix of an inserted word.
    /// </summary>
    public bool TryFindWord(string word, [MaybeNullWhen(false)] out TData data)
    {
        ArgumentNullException.ThrowIfNull(word);

        TrieNode? node = FindNode(word);
        if (node is { HasData: true })
        {
            data = node.Data!;
            return true;
        }

        data = default;
        return false;
    }

    /// <summary>
    /// Returns the value stored under <paramref name="word"/>, or <c>default</c> if there is none.
    /// For value types <c>default</c> is ambiguous (e.g. the first enum member); use <see cref="TryFindWord"/> instead.
    /// </summary>
    public TData? FindWord(string word) => TryFindWord(word, out TData? data) ? data : default;

    /// <summary>Returns <see langword="true"/> if <paramref name="word"/> was inserted.</summary>
    public bool ContainsWord(string word) => TryFindWord(word, out _);

    private TrieNode? FindNode(string word)
    {
        TrieNode current = _root;
        foreach (char letter in word)
        {
            if (!current.Children.TryGetValue(letter, out TrieNode? child))
            {
                return null;
            }

            current = child;
        }

        return current;
    }

    private sealed class TrieNode
    {
        public TData? Data;
        public bool HasData;
        public Dictionary<char, TrieNode> Children { get; } = [];
    }
}
