namespace Bojalib.Sorting;

public static class Bubblesort
{
    /// <summary>Sorts <paramref name="array"/> in place, ascending.</summary>
    public static void Sort<T>(T[] array) where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(array);

        for (int i = array.Length - 1; i > 0; i--)
        {
            for (int o = 0; o < i; o++)
            {
                if (array[o].CompareTo(array[o + 1]) > 0)
                {
                    (array[o], array[o + 1]) = (array[o + 1], array[o]);
                }
            }
        }
    }
}
