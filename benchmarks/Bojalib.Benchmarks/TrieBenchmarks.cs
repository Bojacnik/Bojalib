using BenchmarkDotNet.Attributes;
using Bojalib.Collections;

namespace Bojalib.Benchmarks;

/// <summary><see cref="Trie{TValue}"/> lookups against <see cref="Dictionary{TKey, TValue}"/>.</summary>
[MemoryDiagnoser]
public class TrieBenchmarks
{
    private readonly Trie<int> _trie = new();
    private readonly Dictionary<string, int> _dictionary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _dictionarySpanLookup;
    private string[] _keys = [];
    private const string Text = "the quick brown fox jumps over the lazy dog";

    public TrieBenchmarks() => _dictionarySpanLookup = _dictionary.GetAlternateLookup<ReadOnlySpan<char>>();

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _keys = [.. Enumerable.Range(0, 10_000).Select(_ => new string([.. Enumerable.Range(0, random.Next(3, 12)).Select(_ => (char)('a' + random.Next(26)))]))];
        foreach (string key in _keys.Concat(Text.Split(' ')))
        {
            _trie[key] = key.Length;
            _dictionary[key] = key.Length;
        }
    }

    [Benchmark(Baseline = true)]
    public int DictionaryLookup()
    {
        int sum = 0;
        foreach (string key in _keys)
        {
            sum += _dictionary[key];
        }

        return sum;
    }

    [Benchmark]
    public int TrieLookup()
    {
        int sum = 0;
        foreach (string key in _keys)
        {
            sum += _trie[key];
        }

        return sum;
    }

    /// <summary>Tokenizer-style lookups of slices of a larger string: no substrings are allocated.</summary>
    [Benchmark]
    public int DictionarySliceLookup()
    {
        int sum = 0;
        foreach (Range word in Text.AsSpan().Split(' '))
        {
            sum += _dictionarySpanLookup[Text.AsSpan()[word]];
        }

        return sum;
    }

    [Benchmark]
    public int TrieSliceLookup()
    {
        int sum = 0;
        foreach (Range word in Text.AsSpan().Split(' '))
        {
            sum += _trie[Text.AsSpan()[word]];
        }

        return sum;
    }

    [Benchmark]
    public int TriePrefixSearch() => _trie.EnumeratePrefix("ab").Count();

    [Benchmark]
    public int DictionaryPrefixSearch() => _dictionary.Keys.Count(static k => k.StartsWith("ab", StringComparison.Ordinal));
}
