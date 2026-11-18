using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

public class SortedSpanTests
{
    [Test]
    public void Bounds_MatchLinearScan()
    {
        Gen.Int[-20, 20].Array[0, 300].Select(Gen.Int[-25, 25], static (a, v) => (Sorted: a.Order().ToArray(), Value: v)).Sample(t =>
        {
            int lower = t.Sorted.Count(x => x < t.Value);
            int upper = t.Sorted.Count(x => x <= t.Value);

            SortedSpan.LowerBound<int>(t.Sorted, t.Value).Should().Be(lower);
            SortedSpan.UpperBound<int>(t.Sorted, t.Value).Should().Be(upper);
            SortedSpan.EqualRange<int>(t.Sorted, t.Value).Should().Be(new Range(lower, upper));
            SortedSpan.LowerBound(t.Sorted, t.Value, Comparer<int>.Default).Should().Be(lower);
            SortedSpan.UpperBound(t.Sorted, t.Value, null).Should().Be(upper);
            SortedSpan.EqualRange(t.Sorted, t.Value, Comparer<int>.Default).Should().Be(new Range(lower, upper));
        }, iter: TestScale.Iterations(2000));
    }

    [Test]
    public void Bounds_FloatsWithNaN()
    {
        double[] sorted = [double.NaN, -1, 0, 0, 2];

        SortedSpan.LowerBound<double>(sorted, 0).Should().Be(2);
        SortedSpan.UpperBound<double>(sorted, 0).Should().Be(4);
        SortedSpan.LowerBound<double>(sorted, double.NaN).Should().Be(0);
    }

    [Test]
    public void IsSorted_MatchesPairwiseCheck()
    {
        Gen.Int[0, 3].Array[0, 100].Sample(a =>
        {
            bool expected = a.Zip(a.Skip(1)).All(static p => p.First <= p.Second);
            SortedSpan.IsSorted<int>(a).Should().Be(expected);
            SortedSpan.IsSorted(a, Comparer<int>.Default).Should().Be(expected);
        }, iter: TestScale.Iterations(3000));
    }

    [Test]
    public void IsSorted_Floats_NaNFirstIsSorted()
    {
        SortedSpan.IsSorted<double>([double.NaN, 1, 2]).Should().BeTrue();
        SortedSpan.IsSorted<double>([1, double.NaN]).Should().BeFalse();
        SortedSpan.IsSorted<string>(["a", "b"]).Should().BeTrue();
    }

    [Test]
    public void IsSorted_AtAndAroundTheVectorWidth()
    {
        // The SIMD path starts above one vector of elements; check every length near that boundary.
        for (int length = 0; length <= 40; length++)
        {
            int[] sorted = [.. Enumerable.Range(0, length)];
            SortedSpan.IsSorted<int>(sorted).Should().BeTrue("length {0}", length);
            for (int swap = 1; swap < length; swap++)
            {
                int[] unsorted = [.. sorted];
                (unsorted[swap - 1], unsorted[swap]) = (unsorted[swap], unsorted[swap - 1]);
                SortedSpan.IsSorted<int>(unsorted).Should().BeFalse("length {0}, swapped at {1}", length, swap);
            }
        }

        byte[] bytes = [.. Enumerable.Range(0, 33).Select(static i => (byte)i)];
        SortedSpan.IsSorted<byte>(bytes).Should().BeTrue();
        bytes[32] = 0;
        SortedSpan.IsSorted<byte>(bytes).Should().BeFalse();
    }

    [Test]
    public void Bounds_ManyNaNs_UseNaNAwareOrdering()
    {
        // A raw '<' treats NaN as "not less", which sends the search the wrong way when probes land on NaNs.
        double[] sorted = [double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 1, 2, 2, 3];

        SortedSpan.LowerBound<double>(sorted, 1).Should().Be(5);
        SortedSpan.UpperBound<double>(sorted, 2).Should().Be(8);
        SortedSpan.EqualRange<double>(sorted, 2).Should().Be(new Range(6, 8));
        SortedSpan.LowerBound<double>(sorted, double.NaN).Should().Be(0);
        SortedSpan.UpperBound<double>(sorted, double.NaN).Should().Be(5);
        SortedSpan.IsSorted<double>(sorted).Should().BeTrue();
        SortedSpan.IsSorted<double>([1, double.NaN, 2]).Should().BeFalse();
    }

    [Test]
    public void ComparerOverloads_UseTheGivenComparer()
    {
        IComparer<int> descending = Comparer<int>.Create(static (a, b) => b.CompareTo(a));
        int[] sorted = [9, 7, 7, 7, 4, 1];

        SortedSpan.LowerBound(sorted, 7, descending).Should().Be(1);
        SortedSpan.UpperBound(sorted, 7, descending).Should().Be(4);
        SortedSpan.EqualRange(sorted, 7, descending).Should().Be(new Range(1, 4));
        SortedSpan.IsSorted(sorted, descending).Should().BeTrue();
        SortedSpan.IsSorted<int>(sorted).Should().BeFalse();
        SortedSpan.LowerBound(sorted, 7, null).Should().Be(0, "null means the default (ascending) comparer");
    }
}
