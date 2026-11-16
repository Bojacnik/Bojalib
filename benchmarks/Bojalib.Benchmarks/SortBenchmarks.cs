using BenchmarkDotNet.Attributes;
using Bojalib.Sorting;

namespace Bojalib.Benchmarks;

/// <summary>Bojalib's sorts against the runtime's own <see cref="MemoryExtensions.Sort{T}(Span{T})"/>.</summary>
[MemoryDiagnoser]
public class SortBenchmarks
{
    private int[] _source = [];
    private int[] _work = [];

    [Params(100, 10_000, 1_000_000)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _source = [.. Enumerable.Range(0, Length).Select(_ => random.Next())];
        _work = new int[Length];
    }

    // Each benchmark sorts a fresh copy of the same random data. The copy is a memcpy, identical for every
    // method and tiny next to the sort (IterationSetup would be cleaner but is inaccurate for fast runs).

    [Benchmark(Baseline = true)]
    public void RuntimeSort()
    {
        _source.CopyTo(_work, 0);
        _work.AsSpan().Sort();
    }

    [Benchmark]
    public void QuickSort()
    {
        _source.CopyTo(_work, 0);
        Sorting.QuickSort.Sort(_work);
    }

    [Benchmark]
    public void HeapSort()
    {
        _source.CopyTo(_work, 0);
        Sorting.HeapSort.Sort(_work);
    }
}
