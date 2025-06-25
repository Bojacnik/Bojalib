namespace Bojalib.Sorting;

public static class Bubblesort
{
    public static void Sort(int[] pole)
    {
        bubblesort(pole, 0, pole.Length - 1);
    }

    private static void bubblesort(int[] pole, int left, int right)
    {
        for (int i = right; i > left; i--)
        {
            for (int o = left; o < i; o++)
            {
                if (pole[o] > pole[o + 1])
                {
                    (pole[o], pole[o + 1]) = (pole[o + 1], pole[o]);
                }
            }
        }
    }
}