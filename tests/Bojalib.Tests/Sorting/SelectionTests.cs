using Bojalib.Sorting;
using CsCheck;

namespace Bojalib.Tests.Sorting;

public class SelectionTests
{
    [Test]
    public void Select_PutsNthElementInPlaceWithPartitionAroundIt()
    {
        Gen.Int[-100, 100].Array[1, 3000].SelectMany(a => Gen.Int[0, a.Length - 1].Select(i => (a, i))).Sample(t =>
        {
            (int[] array, int index) = t;
            int[] sorted = [.. array.Order()];

            Selection.Select<int>(array, index);

            array[index].Should().Be(sorted[index]);
            array.Take(index).Should().OnlyContain(x => x <= array[index]);
            array.Skip(index + 1).Should().OnlyContain(x => x >= array[index]);
        }, iter: TestScale.Iterations(300));
    }

    [Test]
    public void Select_AllEqual_IsFast()
    {
        int[] array = new int[1_000_000];

        Selection.Select<int>(array, 500_000);

        array[500_000].Should().Be(0);
    }

    [Test]
    public void Select_WithNaNs_TreatsThemAsSmallest()
    {
        double[] array = [3, double.NaN, 1, double.NaN, 2];

        Selection.Select<double>(array, 0);
        double.IsNaN(array[0]).Should().BeTrue();

        Selection.Select<double>(array, 2);
        array[2].Should().Be(1);
    }

    [Test]
    public void Select_ComparerAndComparison()
    {
        string[] array = ["pear", "fig", "apple", "banana"];

        Selection.Select(array, 0, StringComparer.Ordinal);
        array[0].Should().Be("apple");

        Selection.Select<string>(array, 0, static (a, b) => b.Length.CompareTo(a.Length));
        array[0].Length.Should().Be(6);
    }

    [Test]
    public void Select_IndexOutOfRange_Throws()
    {
        FluentActions.Invoking(() => Selection.Select<int>(new int[3], 3)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Selection.Select<int>(new int[3], -1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void PartialSort_SortsSmallestCountElements()
    {
        Gen.Int.Array[0, 3000].SelectMany(a => Gen.Int[0, a.Length + 5].Select(c => (a, c))).Sample(t =>
        {
            (int[] array, int count) = t;
            int[] expected = [.. array.Order().Take(count)];

            Selection.PartialSort<int>(array, count);

            array.Take(count).Should().Equal(expected);
        }, iter: TestScale.Iterations(300));
    }

    [Test]
    public void PartialSort_ComparerAndComparison_TopK()
    {
        int[] array = [.. Enumerable.Range(0, 1000)];
        Random.Shared.Shuffle(array);

        Selection.PartialSort(array, 3, Comparer<int>.Create(static (a, b) => b.CompareTo(a)));
        array.Take(3).Should().Equal(999, 998, 997);

        Selection.PartialSort<int>(array, 2, static (a, b) => a.CompareTo(b));
        array.Take(2).Should().Equal(0, 1);
    }
}
