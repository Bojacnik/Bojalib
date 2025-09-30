namespace Bojalib.Core.Collections.Generic;

public record TrieNode<TData>
{
    public TData? Data;
    public required Dictionary<char, TrieNode<TData>> Children { get; init; }
}

public record TrieTree<TData>
{
    public TrieTree()
    {
        Root = new TrieNode<TData>
        {
            Children = new Dictionary<char, TrieNode<TData>>()
        };
    }

    public void InsertWord(string word, TData data)
    {
        TrieNode<TData> current = Root;

        for (int i = 0; i < word.Length; i++)
        {
            char letter = word[i];

            if (!current.Children.TryGetValue(letter, out TrieNode<TData>? value))
            {
                value = new TrieNode<TData>
                {
                    Children = new Dictionary<char, TrieNode<TData>>()
                };
                current.Children.Add(letter, value);
            }

            current = value;

            if (i == word.Length - 1)
            {
                current.Data = data;
            }
        }
    }

    public TData? FindWord(string word)
    {
        TrieNode<TData> current = Root;

        for (int i = 0; i < word.Length; i++)
        {
            char letter = word[i];

            if (!current.Children.TryGetValue(letter, out TrieNode<TData>? child))
            {
                return default;
            }
            current = child;

            if (i == word.Length - 1)
            {
                return current.Data;
            }
        }
        return default;
    }

    private TrieNode<TData> Root { get; }
}