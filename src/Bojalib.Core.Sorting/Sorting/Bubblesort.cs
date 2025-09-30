namespace Bojalib.Core.Sorting.Sorting;

public static class Bubblesort
{
    public static void Sort(int[] array)
    {
        Sort(array, 0, array.Length - 1);
    }

    private static void Sort(int[] array, int left, int right)
    {
        for (int i = right; i > left; i--)
        {
            for (int o = left; o < i; o++)
            {
                if (array[o] > array[o + 1])
                {
                    (array[o], array[o + 1]) = (array[o + 1], array[o]);
                }
            }
        }
    }
}