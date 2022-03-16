namespace Bojalib
{
    namespace Sorting
    {
        public static class Quicksort
        {
            public static void Sort(int[] pole)
            {
                quicksort(pole, 0, pole.Length - 1);
            }

            private static void quicksort(int[] pole, int left, int right)
            {
                if (left >= right) return;

                int pivot = pole[(left + right) / 2];

                int i = left, j = right;

                while (i <= j)
                {
                    while (pole[i] < pivot)
                    {
                        i++;
                    }
                    while (pole[j] > pivot)
                    {
                        j--;
                    }

                    if (i <= j)
                    {
                        int temp = pole[i];
                        pole[i] = pole[j];
                        pole[j] = temp;

                        i++; j--;
                    }
                }
                quicksort(pole, left, i - 1);
                quicksort(pole, i, right);
            }
        }
    }
}
