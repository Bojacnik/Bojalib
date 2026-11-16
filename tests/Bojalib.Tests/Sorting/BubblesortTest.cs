using Bojalib.Sorting;
using AwesomeAssertions;

namespace Bojalib.Tests.Sorting;

public class BubblesortTest
{
    [TestCaseSource(typeof(SortTestData), nameof(SortTestData.IntArrays))]
    public void Sort_Ints_SortsAscending(int[] array)
    {
        int[] expected = array.Order().ToArray();

        Bubblesort.Sort(array);

        array.Should().Equal(expected);
    }

    [Test]
    public void Sort_NullElements_SortFirst()
    {
        string?[] array = ["b", null, "a", null];

        Bubblesort.Sort(array);

        array.Should().Equal(null, null, "a", "b");
    }

    [Test]
    public void Sort_WithComparer_UsesComparer()
    {
        int[] array = [3, 1, 2];

        Bubblesort.Sort(array, Comparer<int>.Create((a, b) => b.CompareTo(a)));

        array.Should().Equal(3, 2, 1);
    }

    [Test]
    public void Sort_RandomArrays_MatchesBuiltInSort()
    {
        var random = new Random(12345);
        for (int run = 0; run < 500; run++)
        {
            int[] array = Enumerable.Range(0, random.Next(0, 80)).Select(_ => random.Next(-10, 10)).ToArray();
            int[] expected = array.Order().ToArray();

            Bubblesort.Sort(array);

            array.Should().Equal(expected);
        }
    }

    [Test]
    public void Sort_CompareToReturnsValuesOtherThanOne_SortsAscending()
    {
        Score[] array = [new(50), new(-7), new(3), new(1000), new(3), new(-200)];
        Score[] expected = array.Order().ToArray();

        Bubblesort.Sort(array);

        array.Should().Equal(expected);
    }
}
