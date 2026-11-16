using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class IntervalTreeTests
{
    [Test]
    public void Queries_MatchBruteForce()
    {
        Gen<Interval<int, int>> interval = Gen.Select(Gen.Int[0, 100], Gen.Int[0, 20], Gen.Int)
            .Select(static t => new Interval<int, int>(t.Item1, t.Item1 + t.Item2, t.Item3));
        Gen.Select(interval.Array[0, 200], Gen.Int[-5, 125], Gen.Int[0, 30]).Sample(t =>
        {
            (Interval<int, int>[] intervals, int start, int length) = t;
            int end = start + length;
            var tree = new IntervalTree<int, int>(intervals);

            // Expected: overlapping intervals, ordered by start, ties in input order (the sort is stable).
            var expected = intervals.Where(i => i.Start <= end && i.End >= start).OrderBy(static i => i.Start).ToList();
            tree.Query(start, end).Should().Equal(expected);

            var containing = intervals.Where(i => i.Start <= start && i.End >= start).OrderBy(static i => i.Start).ToList();
            tree.Query(start).Should().Equal(containing);
        }, iter: 2000);
    }

    [Test]
    public void DateOnlyEndpoints()
    {
        var day = new DateOnly(2026, 11, 16);
        var tree = new IntervalTree<DateOnly, string>(
        [
            new(day, day.AddDays(2), "conference"),
            new(day.AddDays(5), day.AddDays(6), "vacation"),
        ]);

        tree.Query(day.AddDays(1)).Select(static i => i.Value).Should().Equal("conference");
        tree.Query(day.AddDays(2), day.AddDays(5)).Select(static i => i.Value).Should().Equal("conference", "vacation");
        tree.Query(day.AddDays(3), day.AddDays(4)).Should().BeEmpty();
    }

    [Test]
    public void BuildValidationEmptyTreeAndEnumeration()
    {
        FluentActions.Invoking(() => new IntervalTree<int, int>([new(5, 1, 0)])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new IntervalTree<int, int>(null!)).Should().Throw<ArgumentNullException>();

        var empty = new IntervalTree<int, int>([]);
        empty.Query(0).Should().BeEmpty();
        empty.Count.Should().Be(0);

        var tree = new IntervalTree<double, string>([new(3, 4, "b"), new(1, 2, "a")]);
        tree.Select(static i => i.Value).Should().Equal("a", "b");
        tree.Intervals.Length.Should().Be(2);
        tree.Invoking(t => t.Query(2, 1)).Should().Throw<ArgumentException>();

        IntervalTree<double, string>.QueryEnumerator e = tree.Query(0, 10).GetEnumerator();
        e.MoveNext();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.Current.Value.Should().Be("a");
    }

    [Test]
    public void LargeTree_QueryVisitsFewNodes()
    {
        var tree = new IntervalTree<int, int>(Enumerable.Range(0, 1_000_000).Select(static i => new Interval<int, int>(i * 10, (i * 10) + 5, i)));

        tree.Query(5_000_003).Select(static i => i.Value).Should().Equal(500_000);
    }
}
