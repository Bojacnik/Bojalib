using System.Numerics;

namespace Bojalib.Sorting;

/// <summary>
/// In-place introspective quicksort: median-of-three quicksort that switches to insertion sort for small ranges
/// and to heapsort if recursion gets too deep, so it is O(n log n) in the worst case.
/// </summary>
/// <remarks>The sort is not stable. <see langword="null"/> elements sort before all other elements.</remarks>
public static class QuickSort
{
    private const int InsertionSortThreshold = 16;

    /// <summary>Sorts <paramref name="span"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    public static void Sort<T>(Span<T> span) where T : IComparable<T>? =>
        SortCore(span, default(DefaultComparer<T>));

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparer">The comparer to use, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public static void Sort<T>(Span<T> span, IComparer<T>? comparer)
    {
        if (comparer is null)
        {
            SortCore(span, default(DefaultComparer<T>));
        }
        else
        {
            SortCore(span, new WrappedComparer<T>(comparer));
        }
    }

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparison"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparison">The comparison to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public static void Sort<T>(Span<T> span, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        SortCore(span, new ComparisonComparer<T>(comparison));
    }

    /// <summary>
    /// Sorts <paramref name="span"/> using a comparer whose type is known at compile time. Passing a
    /// <see langword="struct"/> comparer lets the JIT inline every comparison.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TComparer">The comparer type; ideally a <see langword="struct"/>.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparer">The comparer to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparer"/> is <see langword="null"/>.</exception>
    public static void Sort<T, TComparer>(Span<T> span, TComparer comparer) where TComparer : IComparer<T>
    {
        if (comparer is null)
        {
            throw new ArgumentNullException(nameof(comparer));
        }

        SortCore(span, comparer);
    }

    private static void SortCore<T, TComparer>(Span<T> span, TComparer comparer) where TComparer : IComparer<T>
    {
        if (span.Length < 2)
        {
            return;
        }

        IntroSort(span, 2 * BitOperations.Log2((uint)span.Length), comparer);
    }

    internal static void IntroSort<T, TComparer>(Span<T> span, int depthLimit, TComparer comparer)
        where TComparer : IComparer<T>
    {
        while (span.Length > InsertionSortThreshold)
        {
            if (depthLimit-- == 0)
            {
                HeapSort.SortCore(span, comparer);
                return;
            }

            Partition(span, comparer, out int leftEnd, out int rightStart);
            Span<T> left = span[..leftEnd];
            Span<T> right = span[rightStart..];

            // Recurse into the smaller half and loop on the larger one: stack depth stays O(log n).
            if (left.Length < right.Length)
            {
                IntroSort(left, depthLimit, comparer);
                span = right;
            }
            else
            {
                IntroSort(right, depthLimit, comparer);
                span = left;
            }
        }

        InsertionSort.SortCore(span, comparer);
    }

    /// <summary>Hoare partition around a median-of-three pivot.</summary>
    /// <param name="span">The range to partition.</param>
    /// <param name="comparer">The comparer.</param>
    /// <param name="leftEnd">Exclusive end of the part that is &lt;= pivot.</param>
    /// <param name="rightStart">Start of the part that is &gt;= pivot.</param>
    private static void Partition<T, TComparer>(Span<T> span, TComparer comparer, out int leftEnd, out int rightStart)
        where TComparer : IComparer<T>
    {
        int last = span.Length - 1;
        int middle = last / 2;

        SwapIfGreater(span, 0, middle, comparer);
        SwapIfGreater(span, 0, last, comparer);
        SwapIfGreater(span, middle, last, comparer);

        T pivot = span[middle];
        int i = 0;
        int j = last;

        while (i <= j)
        {
            while (comparer.Compare(span[i], pivot) < 0)
            {
                i++;
            }

            while (comparer.Compare(span[j], pivot) > 0)
            {
                j--;
            }

            if (i <= j)
            {
                (span[i], span[j]) = (span[j], span[i]);
                i++;
                j--;
            }
        }

        leftEnd = j + 1;
        rightStart = i;
    }

    private static void SwapIfGreater<T, TComparer>(Span<T> span, int a, int b, TComparer comparer)
        where TComparer : IComparer<T>
    {
        if (comparer.Compare(span[a], span[b]) > 0)
        {
            (span[a], span[b]) = (span[b], span[a]);
        }
    }
}
