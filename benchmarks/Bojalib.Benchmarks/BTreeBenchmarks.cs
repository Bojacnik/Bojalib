using BenchmarkDotNet.Attributes;
using Bojalib.Collections;

namespace Bojalib.Benchmarks;

/// <summary><see cref="BTreeDictionary{TKey, TValue}"/> against <see cref="SortedDictionary{TKey, TValue}"/> with 100K random int keys.</summary>
[MemoryDiagnoser]
public class BTreeBenchmarks
{
    private int[] _keys = [];
    private readonly BTreeDictionary<int, int> _tree = new();
    private readonly SortedDictionary<int, int> _sorted = new();

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _keys = [.. Enumerable.Range(0, 100_000).Select(_ => random.Next())];
        foreach (int key in _keys)
        {
            _tree[key] = key;
            _sorted[key] = key;
        }
    }

    [Benchmark(Baseline = true)]
    public SortedDictionary<int, int> SortedDictionaryBuild()
    {
        var map = new SortedDictionary<int, int>();
        foreach (int key in _keys)
        {
            map[key] = key;
        }

        return map;
    }

    [Benchmark]
    public BTreeDictionary<int, int> BTreeBuild()
    {
        var map = new BTreeDictionary<int, int>();
        foreach (int key in _keys)
        {
            map[key] = key;
        }

        return map;
    }

    [Benchmark]
    public long SortedDictionaryLookup()
    {
        long sum = 0;
        foreach (int key in _keys)
        {
            sum += _sorted[key];
        }

        return sum;
    }

    [Benchmark]
    public long BTreeLookup()
    {
        long sum = 0;
        foreach (int key in _keys)
        {
            sum += _tree[key];
        }

        return sum;
    }

    [Benchmark]
    public long SortedDictionaryIterate()
    {
        long sum = 0;
        foreach (KeyValuePair<int, int> pair in _sorted)
        {
            sum += pair.Value;
        }

        return sum;
    }

    [Benchmark]
    public long BTreeIterate()
    {
        long sum = 0;
        foreach (KeyValuePair<int, int> pair in _tree)
        {
            sum += pair.Value;
        }

        return sum;
    }
}
