namespace Bojalib.Sorting;

/// <summary>
/// In-place, stable bubble sort with early exit. O(n²); included for teaching and tiny inputs —
/// prefer <see cref="QuickSort"/> or <see cref="InsertionSort"/> in real code.
/// </summary>
/// <remarks><see langword="null"/> elements sort before all other elements.</remarks>
public static class BubbleSort
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

    private static void SortCore<T, TComparer>(Span<T> span, TComparer comparer) where TComparer : IComparer<T>
    {
        for (int end = span.Length - 1; end > 0; end--)
        {
            bool swapped = false;
            for (int i = 0; i < end; i++)
            {
                if (comparer.Compare(span[i], span[i + 1]) > 0)
                {
                    (span[i], span[i + 1]) = (span[i + 1], span[i]);
                    swapped = true;
                }
            }

            if (!swapped)
            {
                return;
            }
        }
    }
}
