namespace Bojalib.Sorting;

public static class Quicksort
{
    /// <summary>Sorts <paramref name="array"/> in place, ascending.</summary>
    public static void Sort<T>(T[] array) where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(array);
        Sort(array, 0, array.Length - 1);
    }

    private static void Sort<T>(T[] array, int left, int right) where T : IComparable<T>
    {
        while (left < right)
        {
            T pivot = array[left + (right - left) / 2];
            int i = left, j = right;

            while (i <= j)
            {
                while (array[i].CompareTo(pivot) < 0)
                {
                    i++;
                }

                while (array[j].CompareTo(pivot) > 0)
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

            Sort(array, left, j);
            left = i;
        }
    }
}
