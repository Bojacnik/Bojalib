namespace Bojalib.Sorting;

/// <summary>In-place heapsort. O(n log n) in every case and needs no extra memory, but not stable.</summary>
/// <remarks><see langword="null"/> elements sort before all other elements.</remarks>
public static class HeapSort
{
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

    internal static void SortCore<T, TComparer>(Span<T> span, TComparer comparer) where TComparer : IComparer<T>
    {
        int n = span.Length;
        for (int i = (n / 2) - 1; i >= 0; i--)
        {
            SiftDown(span, i, n, comparer);
        }

        for (int end = n - 1; end > 0; end--)
        {
            (span[0], span[end]) = (span[end], span[0]);
            SiftDown(span, 0, end, comparer);
        }
    }

    private static void SiftDown<T, TComparer>(Span<T> span, int root, int length, TComparer comparer)
        where TComparer : IComparer<T>
    {
        T value = span[root];
        while (true)
        {
            int child = (2 * root) + 1;
            if (child >= length)
            {
                break;
            }

            if (child + 1 < length && comparer.Compare(span[child], span[child + 1]) < 0)
            {
                child++;
            }

            if (comparer.Compare(value, span[child]) >= 0)
            {
                break;
            }

            span[root] = span[child];
            root = child;
        }

        span[root] = value;
    }
}
