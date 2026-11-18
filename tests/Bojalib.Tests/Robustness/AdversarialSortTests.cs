using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Robustness;

public class AdversarialSortTests
{
    /// <summary>
    /// McIlroy's "killer adversary for quicksort" (1999): every element starts as an undecided "gas" value, and each
    /// comparison freezes elements in the order that makes the sort's pivot choices as bad as possible, while staying
    /// a consistent total order. A plain quicksort degrades to ~n²/4 comparisons against it.
    /// </summary>
    private sealed class Adversary
    {
        private readonly int[] _values;
        private readonly int _gas;
        private int _solid;
        private int _candidate;

        public Adversary(int n)
        {
            _gas = n;
            _values = new int[n];
            Array.Fill(_values, _gas);
        }

        public long Comparisons { get; private set; }

        public int ValueOf(int item) => _values[item];

        public int Compare(int x, int y)
        {
            Comparisons++;
            if (_values[x] == _gas && _values[y] == _gas)
            {
                _values[x == _candidate ? x : y] = _solid++;
            }

            if (_values[x] == _gas)
            {
                _candidate = x;
            }
            else if (_values[y] == _gas)
            {
                _candidate = y;
            }

            return _values[x].CompareTo(_values[y]);
        }
    }

    public static IEnumerable<TestCaseData> Sorts()
    {
        yield return new TestCaseData((Action<int[], Comparison<int>>)((a, c) => QuickSort.Sort(a, c))).SetName("QuickSort");
        yield return new TestCaseData((Action<int[], Comparison<int>>)((a, c) => HeapSort.Sort(a, c))).SetName("HeapSort");
        yield return new TestCaseData((Action<int[], Comparison<int>>)((a, c) => MergeSort.Sort(a, c))).SetName("MergeSort");
        yield return new TestCaseData((Action<int[], Comparison<int>>)((a, c) => Selection.PartialSort<int>(a, a.Length, c))).SetName("PartialSort");
    }

    [TestCaseSource(nameof(Sorts))]
    public void KillerAdversary_StaysNLogN(Action<int[], Comparison<int>> sort)
    {
        foreach (int n in (int[])[1_000, 20_000, TestScale.IsLongRun ? 500_000 : 100_000])
        {
            var adversary = new Adversary(n);
            int[] items = [.. Enumerable.Range(0, n)];

            sort(items, adversary.Compare);

            // Sorted by the values the adversary committed to (undecided elements all compare equal, at the end).
            for (int i = 1; i < n; i++)
            {
                adversary.ValueOf(items[i - 1]).Should().BeLessThanOrEqualTo(adversary.ValueOf(items[i]));
            }

            double bound = 4.0 * n * Math.Log2(n);
            adversary.Comparisons.Should().BeLessThan((long)bound,
                "n = {0}: {1:N0} comparisons; a quicksort without a fallback would need ~{2:N0}", n, adversary.Comparisons, (long)n * n / 4);
            TestContext.Out.WriteLine($"n = {n:N0}: {adversary.Comparisons:N0} comparisons ({adversary.Comparisons / (n * Math.Log2(n)):F2} · n log2 n)");
        }
    }

    [Test]
    public void KillerAdversary_SelectStaysNLogN()
    {
        const int n = 100_000;
        var adversary = new Adversary(n);
        int[] items = [.. Enumerable.Range(0, n)];

        Selection.Select(items, n / 2, adversary.Compare);

        adversary.Comparisons.Should().BeLessThan((long)(4.0 * n * Math.Log2(n)));
        int pivot = adversary.ValueOf(items[n / 2]);
        items.Take(n / 2).Should().OnlyContain(i => adversary.ValueOf(i) <= pivot);
        items.Skip((n / 2) + 1).Should().OnlyContain(i => adversary.ValueOf(i) >= pivot);
    }

    // Values that break naive comparisons: NaN (several bit patterns), ±0, ±∞, subnormals, extremes.
    private static readonly double[] SpecialDoubles =
    [
        double.NaN, -double.NaN, BitConverter.Int64BitsToDouble(0x7FF0_0000_0000_0001), 0.0, -0.0,
        double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -double.Epsilon,
        double.MaxValue, double.MinValue, 1.0, -1.0,
    ];

    private static readonly Gen<double[]> SpecialMix =
        Gen.OneOf(Gen.Double, Gen.Int[0, SpecialDoubles.Length - 1].Select(static i => SpecialDoubles[i])).Array[0, 2000];

    // Expected order: Array.Sort (NaNs first); -0.0 and 0.0 compare equal so either order is fine.
    private static void ShouldMatchRuntime(double[] actual, double[] input)
    {
        double[] expected = [.. input];
        Array.Sort(expected);
        actual.Should().Equal(expected, static (a, b) => a.Equals(b) || (a == 0 && b == 0));
    }

    [Test]
    public void SpecialDoubles_EverySortMatchesRuntime()
    {
        SpecialMix.Sample(input =>
        {
            foreach ((string name, Action<double[]> sort) in (ReadOnlySpan<(string, Action<double[]>)>)
            [
                ("QuickSort", static a => QuickSort.Sort<double>(a)),
                ("HeapSort", static a => HeapSort.Sort<double>(a)),
                ("InsertionSort", static a => InsertionSort.Sort<double>(a)),
                ("MergeSort", static a => MergeSort.Sort<double>(a)),
                ("ParallelSort", static a => ParallelSort.Sort(a)),
                ("PartialSort", static a => Selection.PartialSort<double>(a, a.Length)),
                ("ComparerQuickSort", static a => QuickSort.Sort(a, Comparer<double>.Default)),
            ])
            {
                double[] actual = [.. input];
                sort(actual);
                ShouldMatchRuntime(actual, input);
            }
        }, iter: TestScale.Iterations(300));
    }

    [Test]
    public void SpecialSingles_ThroughEveryVectorPath()
    {
        float[] specials = [float.NaN, -0f, 0f, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon, float.MaxValue, float.MinValue];
        Gen.OneOf(Gen.Float, Gen.Int[0, specials.Length - 1].Select(i => specials[i])).Array[100, 5000].Sample(input =>
        {
            float[] expected = [.. input];
            Array.Sort(expected);
            float[] actual = [.. input];

            QuickSort.Sort<float>(actual);

            actual.Should().Equal(expected, static (a, b) => a.Equals(b) || (a == 0 && b == 0));
        }, iter: TestScale.Iterations(200));
    }

    [Test]
    public void LargeParallelSort_SpecialDoubles()
    {
        var random = new Random(8);
        double[] input = [.. Enumerable.Range(0, 300_000).Select(i => i % 50 == 0 ? SpecialDoubles[random.Next(SpecialDoubles.Length)] : random.NextDouble() - 0.5)];
        double[] actual = [.. input];

        ParallelSort.Sort(actual);

        ShouldMatchRuntime(actual, input);
    }

    [Test]
    public void NullsAndDuplicates_InReferenceTypeSorts()
    {
        Gen.OneOf(Gen.Int[0, 5].Select(static i => (string?)i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Gen.Const((string?)null))
            .Array[0, 500].Sample(input =>
            {
                string?[] expected = [.. input];
                Array.Sort(expected, StringComparer.Ordinal);
                foreach (Action<string?[]> sort in (ReadOnlySpan<Action<string?[]>>)
                [
                    static a => QuickSort.Sort(a, StringComparer.Ordinal),
                    static a => MergeSort.Sort(a, StringComparer.Ordinal),
                    static a => HeapSort.Sort(a, StringComparer.Ordinal),
                    static a => StringSort.Sort(a),
                ])
                {
                    string?[] actual = [.. input];
                    sort(actual);
                    actual.Should().Equal(expected);
                }
            }, iter: TestScale.Iterations(300));
    }
}
