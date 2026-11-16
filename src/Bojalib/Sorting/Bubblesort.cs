namespace Bojalib.Sorting;

public static class Bubblesort
{
    /// <summary>Sorts <paramref name="array"/> in place, ascending. <see langword="null"/> elements sort first.</summary>
    public static void Sort<T>(T[] array) where T : IComparable<T>? => Sort(array, Comparer<T>.Default);

    /// <summary>Sorts <paramref name="array"/> in place using <paramref name="comparer"/>.</summary>
    public static void Sort<T>(T[] array, IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentNullException.ThrowIfNull(comparer);

        for (int i = array.Length - 1; i > 0; i--)
        {
            bool swapped = false;
            for (int o = 0; o < i; o++)
            {
                if (comparer.Compare(array[o], array[o + 1]) > 0)
                {
                    (array[o], array[o + 1]) = (array[o + 1], array[o]);
                    swapped = true;
                }
            }

            if (!swapped)
            {
                return;
            }
        }
    }
}
