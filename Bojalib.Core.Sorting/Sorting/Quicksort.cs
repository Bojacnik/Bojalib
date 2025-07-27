namespace Bojalib.Core.Sorting.Sorting;

public static class Quicksort
{
    public static void Sort(int[] array)
    {
        Sort(array, 0, array.Length - 1);
    }

    
    private static void Sort<TComparable>(TComparable[] array, int left, int right) where TComparable : IComparable<TComparable>, IEquatable<TComparable>
    {
        while (true)
        {
            if (left >= right) return;

            TComparable pivot = array[(left + right) / 2];

            int i = left, j = right;

            while (i <= j)
            {
                int comparison = array[i].CompareTo(pivot);
                while (comparison == -1)
                {
                    i++;
                    comparison = array[i].CompareTo(pivot);
                }

                comparison = array[j].CompareTo(pivot);
                while (comparison == 1)
                {
                    j--;
                    comparison = array[j].CompareTo(pivot);
                }

                if (i > j) continue;

                (array[i], array[j]) = (array[j], array[i]);

                i++;
                j--;
            }

            Sort(array, left, i - 1);
            left = i;
        }
    }
}