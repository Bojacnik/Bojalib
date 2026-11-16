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
        }, iter: 2000);
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
        }, iter: 3000);
    }

    [Test]
    public void IsSorted_Floats_NaNFirstIsSorted()
    {
        SortedSpan.IsSorted<double>([double.NaN, 1, 2]).Should().BeTrue();
        SortedSpan.IsSorted<double>([1, double.NaN]).Should().BeFalse();
        SortedSpan.IsSorted<string>(["a", "b"]).Should().BeTrue();
    }
}
