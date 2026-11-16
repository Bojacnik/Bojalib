namespace Bojalib.Tests.Sorting;

/// <summary>
/// CompareTo returns the raw difference (e.g. -5 or 42) rather than -1/0/1,
/// which the IComparable contract allows.
/// </summary>
public readonly record struct Score(int Value) : IComparable<Score>
{
    public int CompareTo(Score other) => Value - other.Value;
}

public static class SortTestData
{
    public static IEnumerable<int[]> IntArrays()
    {
        yield return [];
        yield return [1];
        yield return [2, 1];
        yield return [2, 3, 1, 7, 3, 20, 5236236, 0, 2];
        yield return [-5, 10, -5, 0, 3, 3, 3, -100];
        yield return [5, 4, 3, 2, 1];
        yield return [1, 2, 3, 4, 5];
    }
}
