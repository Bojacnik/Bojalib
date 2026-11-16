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
    public void Sort_CompareToReturnsValuesOtherThanOne_SortsAscending()
    {
        Score[] array = [new(50), new(-7), new(3), new(1000), new(3), new(-200)];
        Score[] expected = array.Order().ToArray();

        Bubblesort.Sort(array);

        array.Should().Equal(expected);
    }
}
