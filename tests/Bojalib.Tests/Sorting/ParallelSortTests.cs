using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

public class ParallelSortTests
{
    private const int Large = 300_000;

    public static IEnumerable<string> Patterns() =>
        ["random", "sorted", "reversed", "all-equal", "two-values", "few-distinct", "skewed", "organ-pipe"];

    private static int[] Generate(string pattern, int n)
    {
        var random = new Random(7);
        return pattern switch
        {
            "random" => [.. Enumerable.Range(0, n).Select(_ => random.Next())],
            "sorted" => [.. Enumerable.Range(0, n)],
            "reversed" => [.. Enumerable.Range(0, n).Reverse()],
            "all-equal" => new int[n],
            "two-values" => [.. Enumerable.Range(0, n).Select(_ => random.Next(2))],
            "few-distinct" => [.. Enumerable.Range(0, n).Select(_ => random.Next(10))],
            // 90% of the values in a tiny range: one bucket gets most elements and must be split again.
            "skewed" => [.. Enumerable.Range(0, n).Select(_ => random.Next(10) == 0 ? random.Next() : random.Next(1000))],
            "organ-pipe" => [.. Enumerable.Range(0, n).Select(i => i < n / 2 ? i : n - i)],
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    [TestCaseSource(nameof(Patterns))]
    public void Sort_Patterns_MatchBuiltInSort(string pattern)
    {
        int[] array = Generate(pattern, Large);
        int[] expected = [.. array.Order()];

        ParallelSort.Sort(array);

        array.Should().Equal(expected);
    }

    [Test]
    public void Sort_EveryThreadCount_MatchesBuiltInSort()
    {
        foreach (int threads in (int[])[1, 2, 3, Environment.ProcessorCount, -1])
        {
            int[] array = Generate("random", Large);
            int[] expected = [.. array.Order()];

            ParallelSort.Sort(array, threads);

            array.Should().Equal(expected, "threads = {0}", threads);
        }
    }

    [Test]
    public void Sort_RandomSizesAroundThreshold()
    {
        Gen.Int[60_000, 140_000].Select(Gen.Int[1, 1_000_000]).Sample(t =>
        {
            (int n, int range) = t;
            var random = new Random(n);
            long[] array = [.. Enumerable.Range(0, n).Select(_ => (long)random.Next(range) - (range / 2))];
            long[] expected = [.. array.Order()];

            ParallelSort.Sort(array);

            array.Should().Equal(expected);
        }, iter: 20, threads: 1);
    }

    [Test]
    public void Sort_DoublesWithNaN()
    {
        var random = new Random(3);
        double[] array = [.. Enumerable.Range(0, Large).Select(i => i % 97 == 0 ? double.NaN : random.NextDouble() - 0.5)];
        double[] expected = [.. array];
        Array.Sort(expected);

        ParallelSort.Sort(array);

        array.Should().Equal(expected, static (a, b) => a.Equals(b));
    }

    [Test]
    public void Sort_StringsWithComparerAndComparison()
    {
        var random = new Random(5);
        string[] words = [.. Enumerable.Range(0, Large).Select(_ => random.Next(50_000).ToString("x", System.Globalization.CultureInfo.InvariantCulture))];
        string[] expected = [.. words];
        Array.Sort(expected, StringComparer.Ordinal);

        string[] byComparer = [.. words];
        ParallelSort.Sort(byComparer, StringComparer.Ordinal);
        byComparer.Should().Equal(expected);

        string[] byComparison = [.. words];
        ParallelSort.Sort(byComparison, string.CompareOrdinal);
        byComparison.Should().Equal(expected);

        string[] descending = [.. words];
        ParallelSort.Sort(descending.AsMemory(), (a, b) => string.CompareOrdinal(b, a), maxDegreeOfParallelism: 2);
        descending.Should().Equal(expected.Reverse());
    }

    [Test]
    public void Sort_MemorySlice_OnlySortsTheSlice()
    {
        int[] array = Generate("random", Large);
        int[] original = [.. array];

        ParallelSort.Sort(array.AsMemory(1000, Large - 2000));

        array.AsSpan(0, 1000).ToArray().Should().Equal(original.Take(1000));
        array.AsSpan(Large - 1000).ToArray().Should().Equal(original.Skip(Large - 1000));
        array.AsSpan(1000, Large - 2000).ToArray().Should().Equal(original.Skip(1000).Take(Large - 2000).Order());
    }

    [Test]
    public void Sort_SmallAndCountingSortTypes_UseSequentialPath()
    {
        int[] small = [3, 1, 2];
        ParallelSort.Sort(small);
        small.Should().Equal(1, 2, 3);

        byte[] bytes = [.. Enumerable.Range(0, Large).Select(i => (byte)(i * 7))];
        ParallelSort.Sort(bytes);
        bytes.Should().BeInAscendingOrder();

        string[] few = ["b", "a"];
        ParallelSort.Sort(few, (IComparer<string>?)null);
        few.Should().Equal("a", "b");
    }

    [Test]
    public void ArgumentsAreValidated()
    {
        FluentActions.Invoking(() => ParallelSort.Sort(new int[1], 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ParallelSort.Sort(new int[1], -2)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ParallelSort.Sort((int[])null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ParallelSort.Sort((int[])null!, Comparer<int>.Default)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ParallelSort.Sort((int[])null!, static (a, b) => 0)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ParallelSort.Sort(new int[1], (Comparison<int>)null!)).Should().Throw<ArgumentNullException>();
    }
}
