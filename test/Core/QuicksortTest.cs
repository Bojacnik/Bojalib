using System.Linq;
using FluentAssertions;

namespace Core;

public class Tests
{
    private int[] _array;
    [SetUp]
    public void Setup()
    {
        _array = [2, 3, 1, 7, 3, 20, 5236236, 0, 2];
    }

    [Test]
    public void SortTest()
    {
        // Arrange
        int[] expected = [0, 1, 2, 2, 3, 3, 7, 20, 5236236];

        // Act
        Bojalib.Sorting.Quicksort.Sort(_array);
        
        // Assert
        bool result = _array.SequenceEqual(expected);

        result.Should().Be(true);
    }
}