namespace Bojalib.Sorting;

/// <summary>In-place, stable insertion sort. O(n²), but the fastest choice for small or nearly sorted inputs.</summary>
/// <remarks><see langword="null"/> elements sort before all other elements.</remarks>
public static class InsertionSort
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
        for (int i = 1; i < span.Length; i++)
        {
            T current = span[i];
            int j = i - 1;
            while (j >= 0 && comparer.Compare(span[j], current) > 0)
            {
                span[j + 1] = span[j];
                j--;
            }

            span[j + 1] = current;
        }
    }
}
