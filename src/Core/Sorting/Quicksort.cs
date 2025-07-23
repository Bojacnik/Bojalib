using System;

namespace Bojalib.Core.Sorting;

public static class Quicksort
{
    public static void Sort(int[] pole)
    {
        Sort(pole, 0, pole.Length - 1);
    }

    
    private static void Sort<TComparable>(TComparable[] pole, int left, int right) where TComparable : IComparable<TComparable>, IEquatable<TComparable>
    {
        while (true)
        {
            if (left >= right) return;

            TComparable pivot = pole[(left + right) / 2];

            int i = left, j = right;

            while (i <= j)
            {
                int comparison = pole[i].CompareTo(pivot);
                while (comparison == -1)
                {
                    i++;
                    comparison = pole[i].CompareTo(pivot);
                }

                comparison = pole[j].CompareTo(pivot);
                while (comparison == 1)
                {
                    j--;
                    comparison = pole[j].CompareTo(pivot);
                }

                if (i > j) continue;

                (pole[i], pole[j]) = (pole[j], pole[i]);

                i++;
                j--;
            }

            Sort(pole, left, i - 1);
            left = i;
        }
    }
}