using System.Collections.Concurrent;
using Bojalib.Sorting;

namespace Bojalib.Tests.Robustness;

/// <summary>
/// Tests written for mutants that survived a Stryker run on Bojalib.Sorting: argument checks on every overload,
/// paths no test reached, and "performance" mutants (more comparisons, no parallelism) that correctness checks
/// alone can't see.
/// </summary>
public class MutationGapTests
{
    private static int[] RandomInts(int n, int seed, int range = int.MaxValue)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, n).Select(_ => random.Next(range))];
    }

    // ---- Selection ----

    [Test]
    public void Selection_EveryOverloadValidatesArguments()
    {
        IComparer<int> comparer = Comparer<int>.Default;
        Comparison<int> comparison = static (a, b) => a.CompareTo(b);

        FluentActions.Invoking(() => Selection.Select(new int[3], -1, comparer)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.Select(new int[3], 3, comparer)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.Select(new int[3], -1, comparison)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.Select(new int[3], 3, comparison)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.Select(new int[3], 0, (Comparison<int>)null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Selection.PartialSort<int>(new int[3], -1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.PartialSort(new int[3], -1, comparer)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.PartialSort(new int[3], -1, comparison)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.PartialSort(new int[3], 1, (Comparison<int>)null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Selection_ComparerOverloadsHonourTheComparerAndCount()
    {
        IComparer<int> descending = Comparer<int>.Create(static (a, b) => b.CompareTo(a));
        foreach (int count in (int[])[0, 1, 3, 999, 1000, 1001, 5000])
        {
            int[] data = RandomInts(1000, count);
            int[] expected = [.. data.OrderDescending().Take(count)];

            int[] byComparer = [.. data];
            Selection.PartialSort(byComparer, count, descending);
            byComparer.Take(Math.Min(count, 1000)).Should().Equal(expected, "comparer, count {0}", count);

            int[] byComparison = [.. data];
            Selection.PartialSort<int>(byComparison, count, static (a, b) => b.CompareTo(a));
            byComparison.Take(Math.Min(count, 1000)).Should().Equal(expected, "comparison, count {0}", count);

            if (count == 0)
            {
                byComparer.Should().Equal(data, "count 0 must leave the data alone");
            }
        }

        int[] select = RandomInts(1000, 7);
        Selection.Select(select, 10, descending);
        select[10].Should().Be(select.OrderDescending().ElementAt(10));
    }

    [Test]
    public void Selection_IsLinearNotASort()
    {
        // Introselect does ~2-4 comparisons per element; a full sort of 100K would need ~17.
        foreach ((string name, int[] data) in (ReadOnlySpan<(string, int[])>)[("random", RandomInts(100_000, 1)), ("few distinct", RandomInts(100_000, 2, 5)), ("sorted", [.. Enumerable.Range(0, 100_000)])])
        {
            long comparisons = 0;
            int[] work = [.. data];
            Selection.Select(work, 50_000, (a, b) => { comparisons++; return a.CompareTo(b); });
            work[50_000].Should().Be(data.Order().ElementAt(50_000));
            comparisons.Should().BeLessThan(6L * data.Length, "{0}: {1:N0} comparisons", name, comparisons);

            comparisons = 0;
            work = [.. data];
            Selection.PartialSort(work, 10, (a, b) => { comparisons++; return a.CompareTo(b); });
            work.Take(10).Should().Equal(data.Order().Take(10));
            comparisons.Should().BeLessThan(6L * data.Length, "{0} top-10: {1:N0} comparisons", name, comparisons);
        }
    }

    [Test]
    public void Selection_NaNsBeforeAndAfterTheIndex()
    {
        double[] data = [double.NaN, 5, double.NaN, 1, double.NaN, 3];
        for (int index = 0; index < data.Length; index++)
        {
            double[] work = [.. data];
            Selection.Select<double>(work, index);
            double[] sorted = [.. data];
            Array.Sort(sorted);
            work[index].Should().Be(sorted[index], "index {0}", index);
        }
    }

    // ---- ParallelSort ----

    [Test]
    public void ParallelSort_AllEqualWithComparer_TakesTheNoSplitterPath()
    {
        // Default-order all-equal input is caught by the "already sorted" check; a comparer skips that check.
        int[] equal = new int[300_000];
        ParallelSort.Sort(equal, Comparer<int>.Default);
        equal.Should().OnlyContain(static x => x == 0);

        (int Key, int Id)[] pairs = [.. Enumerable.Range(0, 300_000).Select(static i => (7, i))];
        ParallelSort.Sort(pairs, static (a, b) => a.Key.CompareTo(b.Key));
        pairs.Should().OnlyContain(static p => p.Key == 7);
        pairs.Select(static p => p.Id).Order().Should().Equal(Enumerable.Range(0, 300_000), "no element lost or duplicated");
    }

    [Test]
    public void ParallelSort_BadlySkewedSample_ResplitsTheOversizedBucket()
    {
        // Values at the sampled positions are spread out, everything else is squeezed between two adjacent
        // splitters, so one bucket gets almost all elements and must be split again recursively.
        const int n = 300_000;
        int threads = Math.Min(4, Environment.ProcessorCount);
        int[] sampled = ParallelSort.SamplePositions(n, threads);
        var random = new Random(5);
        double[] data = [.. Enumerable.Range(0, n).Select(_ => 161 + (random.NextDouble() * 158))];
        for (int k = 0; k < sampled.Length; k++)
        {
            data[sampled[k]] = k * 10;
        }

        double[] expected = [.. data];
        Array.Sort(expected);

        ParallelSort.Sort(data, threads);

        data.AsSpan().SequenceEqual(expected).Should().BeTrue();
    }

    [Test]
    public void ParallelSort_UsesSeveralThreadsOnlyWhenAllowed()
    {
        if (Environment.ProcessorCount < 2)
        {
            Assert.Ignore("Needs more than one processor.");
        }

        var threadIds = new ConcurrentDictionary<int, bool>();
        int[] data = RandomInts(300_000, 3);
        ParallelSort.Sort(data, (a, b) => { threadIds.TryAdd(Environment.CurrentManagedThreadId, true); return a.CompareTo(b); });
        threadIds.Count.Should().BeGreaterThan(1, "a large sort should run on more than one thread");
        data.Should().BeInAscendingOrder();

        threadIds.Clear();
        data = RandomInts(300_000, 4);
        ParallelSort.Sort(data.AsMemory(), (a, b) => { threadIds.TryAdd(Environment.CurrentManagedThreadId, true); return a.CompareTo(b); }, maxDegreeOfParallelism: 1);
        threadIds.Keys.Should().Equal([Environment.CurrentManagedThreadId], "maxDegreeOfParallelism 1 sorts on the calling thread");
    }

    // ---- StringSort ----

    [Test]
    public void StringSort_NulCharactersSortBeforeEverythingButEndOfString()
    {
        string?[] input = ["a\0", "a", "\0", "", "a\0\0", "\0\0", "b", "a\u0001"];
        string?[] expected = [.. input];
        Array.Sort(expected, StringComparer.Ordinal);

        StringSort.Sort(input);

        input.Should().Equal(expected);
    }

    [Test]
    public void StringSort_TwoKeysWithItems_AreSorted()
    {
        string?[] keys = ["b", "a"];
        int[] items = [2, 1];

        StringSort.Sort(keys.AsSpan(), items.AsSpan());

        keys.Should().Equal("a", "b");
        items.Should().Equal(1, 2);
    }

    [Test]
    public void StringSort_ManyPendingPartitions_GrowTheWorkStack()
    {
        // At every depth of a long shared prefix, two strings branch lower and two branch higher: each step leaves
        // two partitions pending, far more than the initial stack holds.
        string prefix = new('m', 300);
        var strings = new List<string?>();
        for (int depth = 0; depth < prefix.Length; depth++)
        {
            string stem = prefix[..depth];
            strings.AddRange([stem + "a0", stem + "a1", stem + "z0", stem + "z1"]);
        }

        strings.Add(prefix);
        string?[] input = [.. strings];
        string?[] expected = [.. input];
        Array.Sort(expected, StringComparer.Ordinal);

        StringSort.Sort(input);

        input.Should().Equal(expected);
    }

    // ---- Default ordering for every built-in type (Less.cs) ----

    [Test]
    public void DefaultOrdering_EveryIntegerType_InEveryComparisonSort()
    {
        static void Check<T>(T[] data) where T : IComparable<T>
        {
            T[] expected = [.. data];
            Array.Sort(expected);
            foreach (Action<T[]> sort in (ReadOnlySpan<Action<T[]>>)[static a => InsertionSort.Sort<T>(a), static a => HeapSort.Sort<T>(a), static a => MergeSort.Sort<T>(a), static a => BubbleSort.Sort<T>(a), static a => Selection.PartialSort<T>(a, a.Length)])
            {
                T[] actual = [.. data];
                sort(actual);
                actual.Should().Equal(expected, "{0}", typeof(T).Name);
            }
        }

        var random = new Random(9);
        Check([.. Enumerable.Range(0, 300).Select(_ => (byte)random.Next(256))]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (sbyte)random.Next(-128, 128))]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (short)random.Next(short.MinValue, short.MaxValue))]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (ushort)random.Next(ushort.MaxValue))]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (char)random.Next(char.MaxValue))]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (nint)random.NextInt64(long.MinValue, long.MaxValue))]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (nuint)(ulong)random.NextInt64())]);
        Check([.. Enumerable.Range(0, 300).Select(_ => (ushort)random.Next(3))]);
    }

    [Test]
    public void BubbleSort_FloatingPoint_UsesNaNAwareOrdering()
    {
        double[] data = [3, double.NaN, -0.0, double.NegativeInfinity, double.NaN, 1];
        double[] expected = [.. data];
        Array.Sort(expected);

        BubbleSort.Sort<double>(data);

        data.Should().Equal(expected, static (a, b) => a.Equals(b));
    }
}
