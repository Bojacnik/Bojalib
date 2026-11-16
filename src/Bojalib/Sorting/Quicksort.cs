namespace Bojalib.Sorting;

public static class Quicksort
{
    /// <summary>Sorts <paramref name="array"/> in place, ascending. <see langword="null"/> elements sort first.</summary>
    public static void Sort<T>(T[] array) where T : IComparable<T>? => Sort(array, Comparer<T>.Default);

    /// <summary>Sorts <paramref name="array"/> in place using <paramref name="comparer"/>.</summary>
    public static void Sort<T>(T[] array, IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentNullException.ThrowIfNull(comparer);
        Sort(array, 0, array.Length - 1, comparer);
    }

    private static void Sort<T>(T[] array, int left, int right, IComparer<T> comparer)
    {
        while (left < right)
        {
            T pivot = array[left + (right - left) / 2];
            int i = left, j = right;

            while (i <= j)
            {
                while (comparer.Compare(array[i], pivot) < 0)
                {
                    i++;
                }

                while (comparer.Compare(array[j], pivot) > 0)
                {
                    j--;
                }

                if (i > j)
                {
                    break;
                }

                (array[i], array[j]) = (array[j], array[i]);
                i++;
                j--;
            }

            // Recurse into the smaller half and loop on the larger one,
            // so stack depth stays O(log n) even for bad pivots.
            if (j - left < right - i)
            {
                Sort(array, left, j, comparer);
                left = i;
            }
            else
            {
                Sort(array, i, right, comparer);
                right = j;
            }
        }
    }
}
