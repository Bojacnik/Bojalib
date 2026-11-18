using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bojalib.Sorting.Internal;

/// <summary>
/// Pattern-defeating quicksort (Orson Peters, 2016): introsort's worst-case guarantee plus O(n) handling of sorted
/// runs and of many equal keys. Works directly on <c>ref T</c> with no bounds checks; the partition step is
/// vectorized for built-in numeric types (see <see cref="VectorizedPartition"/>).
/// </summary>
internal static class PdqSort
{
    private const int InsertionSortThreshold = 24;
    private const int NintherThreshold = 128;
    private const int PartialInsertionSortLimit = 8;

    public static void Sort<T, TLess>(Span<T> span, TLess less) where TLess : ILess<T>
    {
        if (span.Length < 2)
        {
            return;
        }

        ref T start = ref MemoryMarshal.GetReference(span);
        Loop(ref start, 0, span.Length, less, BitOperations.Log2((uint)span.Length), leftmost: true);
    }

    /// <summary>Whether partitions can use SIMD: default ordering on a vectorizable type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool UseVectorPartition<T, TLess>(int size) where TLess : ILess<T> =>
        typeof(TLess) == typeof(DefaultLess<T>) && size >= VectorizedPartition.MinLength && VectorizedPartition.IsSupported<T>();

    // `leftmost`: no element exists before `begin` in the original span. Otherwise the element at begin - 1 is
    // <= everything in [begin, end), which lets insertion sort and partition_left skip bounds checks.
    private static void Loop<T, TLess>(ref T r, int begin, int end, TLess less, int badAllowed, bool leftmost)
        where TLess : ILess<T>
    {
        while (true)
        {
            int size = end - begin;
            if (size < InsertionSortThreshold)
            {
                if (leftmost)
                {
                    InsertionSort(ref r, begin, end, less);
                }
                else
                {
                    UnguardedInsertionSort(ref r, begin, end, less);
                }

                return;
            }

            // Pivot selection: median of 3, or Tukey's ninther for large ranges. The pivot ends up at `begin`.
            int half = size / 2;
            if (size > NintherThreshold)
            {
                Sort3(ref r, begin, begin + half, end - 1, less);
                Sort3(ref r, begin + 1, begin + (half - 1), end - 2, less);
                Sort3(ref r, begin + 2, begin + (half + 1), end - 3, less);
                Sort3(ref r, begin + (half - 1), begin + half, begin + (half + 1), less);
                Swap(ref r, begin, begin + half);
            }
            else
            {
                Sort3(ref r, begin + half, begin, end - 1, less);
            }

            // If the pivot equals the element before this range, everything <= pivot is equal to it and already
            // in its final place: put those left and only continue with the elements greater than the pivot.
            if (!leftmost && !less.Less(Unsafe.Add(ref r, begin - 1), Unsafe.Add(ref r, begin)))
            {
                begin = PartitionLeft(ref r, begin, end, less) + 1;
                continue;
            }

            int pivotPos = PartitionRight(ref r, begin, end, less, out bool alreadyPartitioned);
            int leftSize = pivotPos - begin;
            int rightSize = end - (pivotPos + 1);

            if (leftSize < size / 8 || rightSize < size / 8)
            {
                // Bad partition: after too many, fall back to heapsort for the O(n log n) guarantee. Otherwise
                // shuffle some elements to break up patterns that could cause the next pivot to be bad too.
                if (--badAllowed == 0)
                {
                    HeapSort(ref Unsafe.Add(ref r, begin), size, less);
                    return;
                }

                if (leftSize >= InsertionSortThreshold)
                {
                    Swap(ref r, begin, begin + (leftSize / 4));
                    Swap(ref r, pivotPos - 1, pivotPos - (leftSize / 4));
                    if (leftSize > NintherThreshold)
                    {
                        Swap(ref r, begin + 1, begin + ((leftSize / 4) + 1));
                        Swap(ref r, begin + 2, begin + ((leftSize / 4) + 2));
                        Swap(ref r, pivotPos - 2, pivotPos - ((leftSize / 4) + 1));
                        Swap(ref r, pivotPos - 3, pivotPos - ((leftSize / 4) + 2));
                    }
                }

                if (rightSize >= InsertionSortThreshold)
                {
                    Swap(ref r, pivotPos + 1, pivotPos + (1 + (rightSize / 4)));
                    Swap(ref r, end - 1, end - (rightSize / 4));
                    if (rightSize > NintherThreshold)
                    {
                        Swap(ref r, pivotPos + 2, pivotPos + (2 + (rightSize / 4)));
                        Swap(ref r, pivotPos + 3, pivotPos + (3 + (rightSize / 4)));
                        Swap(ref r, end - 2, end - (1 + (rightSize / 4)));
                        Swap(ref r, end - 3, end - (2 + (rightSize / 4)));
                    }
                }
            }
            else if (alreadyPartitioned
                     && PartialInsertionSort(ref r, begin, pivotPos, less)
                     && PartialInsertionSort(ref r, pivotPos + 1, end, less))
            {
                // The range was (nearly) sorted already.
                return;
            }

            // Recurse into the left part, loop on the right.
            Loop(ref r, begin, pivotPos, less, badAllowed, leftmost);
            begin = pivotPos + 1;
            leftmost = false;
        }
    }

    /// <summary>
    /// Partitions [begin, end) around the pivot at <paramref name="begin"/>: elements &lt; pivot left, &gt;= right.
    /// </summary>
    /// <returns>The pivot's final position.</returns>
    internal static int PartitionRight<T, TLess>(ref T r, int begin, int end, TLess less, out bool alreadyPartitioned)
        where TLess : ILess<T>
    {
        T pivot = Unsafe.Add(ref r, begin);

        if (UseVectorPartition<T, TLess>(end - begin))
        {
            int leftCount = VectorizedPartition.Partition(ref Unsafe.Add(ref r, begin + 1), end - begin - 1, pivot, equalGoesRight: true);
            int position = begin + leftCount;
            Unsafe.Add(ref r, begin) = Unsafe.Add(ref r, position);
            Unsafe.Add(ref r, position) = pivot;
            alreadyPartitioned = false;
            return position;
        }

        int first = begin;
        int last = end;

        // The median-of-3 step guarantees an element >= pivot at the end, so these scans need no bounds checks.
        while (less.Less(Unsafe.Add(ref r, ++first), pivot))
        {
        }

        if (first - 1 == begin)
        {
            while (first < last && !less.Less(Unsafe.Add(ref r, --last), pivot))
            {
            }
        }
        else
        {
            while (!less.Less(Unsafe.Add(ref r, --last), pivot))
            {
            }
        }

        alreadyPartitioned = first >= last;

        while (first < last)
        {
            Swap(ref r, first, last);
            while (less.Less(Unsafe.Add(ref r, ++first), pivot))
            {
            }

            while (!less.Less(Unsafe.Add(ref r, --last), pivot))
            {
            }
        }

        int pivotPos = first - 1;
        Unsafe.Add(ref r, begin) = Unsafe.Add(ref r, pivotPos);
        Unsafe.Add(ref r, pivotPos) = pivot;
        return pivotPos;
    }

    /// <summary>
    /// Partitions [begin, end) around the pivot at <paramref name="begin"/>: elements &lt;= pivot left, &gt; right.
    /// </summary>
    /// <returns>The pivot's final position.</returns>
    internal static int PartitionLeft<T, TLess>(ref T r, int begin, int end, TLess less) where TLess : ILess<T>
    {
        T pivot = Unsafe.Add(ref r, begin);

        if (UseVectorPartition<T, TLess>(end - begin))
        {
            int leftCount = VectorizedPartition.Partition(ref Unsafe.Add(ref r, begin + 1), end - begin - 1, pivot, equalGoesRight: false);
            int position = begin + leftCount;
            Unsafe.Add(ref r, begin) = Unsafe.Add(ref r, position);
            Unsafe.Add(ref r, position) = pivot;
            return position;
        }

        int first = begin;
        int last = end;

        while (less.Less(pivot, Unsafe.Add(ref r, --last)))
        {
        }

        if (last + 1 == end)
        {
            while (first < last && !less.Less(pivot, Unsafe.Add(ref r, ++first)))
            {
            }
        }
        else
        {
            while (!less.Less(pivot, Unsafe.Add(ref r, ++first)))
            {
            }
        }

        while (first < last)
        {
            Swap(ref r, first, last);
            while (less.Less(pivot, Unsafe.Add(ref r, --last)))
            {
            }

            while (!less.Less(pivot, Unsafe.Add(ref r, ++first)))
            {
            }
        }

        int pivotPos = last;
        Unsafe.Add(ref r, begin) = Unsafe.Add(ref r, pivotPos);
        Unsafe.Add(ref r, pivotPos) = pivot;
        return pivotPos;
    }

    /// <summary>Picks a pivot for [begin, end) and moves it to <paramref name="begin"/> (used by selection).</summary>
    internal static void ChoosePivot<T, TLess>(ref T r, int begin, int end, TLess less) where TLess : ILess<T>
    {
        int size = end - begin;
        int half = size / 2;
        if (size > NintherThreshold)
        {
            Sort3(ref r, begin, begin + half, end - 1, less);
            Sort3(ref r, begin + 1, begin + (half - 1), end - 2, less);
            Sort3(ref r, begin + 2, begin + (half + 1), end - 3, less);
            Sort3(ref r, begin + (half - 1), begin + half, begin + (half + 1), less);
            Swap(ref r, begin, begin + half);
        }
        else
        {
            Sort3(ref r, begin + half, begin, end - 1, less);
        }
    }

    internal static void InsertionSort<T, TLess>(ref T r, int begin, int end, TLess less) where TLess : ILess<T>
    {
        for (int i = begin + 1; i < end; i++)
        {
            T current = Unsafe.Add(ref r, i);
            int j = i - 1;
            if (!less.Less(current, Unsafe.Add(ref r, j)))
            {
                continue;
            }

            do
            {
                Unsafe.Add(ref r, j + 1) = Unsafe.Add(ref r, j);
                j--;
            }
            while (j >= begin && less.Less(current, Unsafe.Add(ref r, j)));

            Unsafe.Add(ref r, j + 1) = current;
        }
    }

    // Like InsertionSort, but relies on the element before `begin` being <= every element in the range.
    private static void UnguardedInsertionSort<T, TLess>(ref T r, int begin, int end, TLess less) where TLess : ILess<T>
    {
        for (int i = begin + 1; i < end; i++)
        {
            T current = Unsafe.Add(ref r, i);
            int j = i - 1;
            if (!less.Less(current, Unsafe.Add(ref r, j)))
            {
                continue;
            }

            do
            {
                Unsafe.Add(ref r, j + 1) = Unsafe.Add(ref r, j);
                j--;
            }
            while (less.Less(current, Unsafe.Add(ref r, j)));

            Unsafe.Add(ref r, j + 1) = current;
        }
    }

    // Insertion sort that gives up after moving a few elements: cheap way to finish nearly sorted ranges.
    private static bool PartialInsertionSort<T, TLess>(ref T r, int begin, int end, TLess less) where TLess : ILess<T>
    {
        int moved = 0;
        for (int i = begin + 1; i < end; i++)
        {
            T current = Unsafe.Add(ref r, i);
            int j = i - 1;
            if (!less.Less(current, Unsafe.Add(ref r, j)))
            {
                continue;
            }

            do
            {
                Unsafe.Add(ref r, j + 1) = Unsafe.Add(ref r, j);
                j--;
            }
            while (j >= begin && less.Less(current, Unsafe.Add(ref r, j)));

            Unsafe.Add(ref r, j + 1) = current;
            moved += i - (j + 1);
            if (moved > PartialInsertionSortLimit)
            {
                return false;
            }
        }

        return true;
    }

    internal static void HeapSort<T, TLess>(ref T r, int length, TLess less) where TLess : ILess<T>
    {
        for (int i = (length / 2) - 1; i >= 0; i--)
        {
            SiftDown(ref r, i, length, less);
        }

        for (int end = length - 1; end > 0; end--)
        {
            Swap(ref r, 0, end);
            SiftDown(ref r, 0, end, less);
        }
    }

    private static void SiftDown<T, TLess>(ref T r, int root, int length, TLess less) where TLess : ILess<T>
    {
        T value = Unsafe.Add(ref r, root);
        while (true)
        {
            int child = (2 * root) + 1;
            if (child >= length)
            {
                break;
            }

            if (child + 1 < length && less.Less(Unsafe.Add(ref r, child), Unsafe.Add(ref r, child + 1)))
            {
                child++;
            }

            if (!less.Less(value, Unsafe.Add(ref r, child)))
            {
                break;
            }

            Unsafe.Add(ref r, root) = Unsafe.Add(ref r, child);
            root = child;
        }

        Unsafe.Add(ref r, root) = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Sort2<T, TLess>(ref T r, int a, int b, TLess less) where TLess : ILess<T>
    {
        if (less.Less(Unsafe.Add(ref r, b), Unsafe.Add(ref r, a)))
        {
            Swap(ref r, a, b);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Sort3<T, TLess>(ref T r, int a, int b, int c, TLess less) where TLess : ILess<T>
    {
        Sort2(ref r, a, b, less);
        Sort2(ref r, b, c, less);
        Sort2(ref r, a, b, less);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap<T>(ref T r, int a, int b) =>
        (Unsafe.Add(ref r, a), Unsafe.Add(ref r, b)) = (Unsafe.Add(ref r, b), Unsafe.Add(ref r, a));
}
