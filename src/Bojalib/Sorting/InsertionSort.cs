using System.Runtime.InteropServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>In-place, stable insertion sort. O(n²), but the fastest choice for small or nearly sorted inputs.</summary>
/// <remarks><see langword="null"/>s and NaNs sort first, matching <see cref="Comparer{T}.Default"/>.</remarks>
public static class InsertionSort
{
    /// <summary>Sorts <paramref name="span"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    public static void Sort<T>(Span<T> span) where T : IComparable<T>?
    {
        // Stable: floats use the NaN-aware comparer rather than reordering NaNs up front.
        if (Primitives.IsFloatingPoint<T>())
        {
            Core(span, new ComparerLess<T, Comparer<T>>(Comparer<T>.Default));
        }
        else
        {
            Core(span, default(DefaultLess<T>));
        }
    }

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public static void Sort<T>(Span<T> span, IComparer<T>? comparer) =>
        Core(span, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default));

    /// <summary>Sorts <paramref name="span"/> using <paramref name="comparison"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    /// <param name="comparison">The comparison.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public static void Sort<T>(Span<T> span, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        Core(span, new ComparisonLess<T>(comparison));
    }

    private static void Core<T, TLess>(Span<T> span, TLess less) where TLess : ILess<T> =>
        PdqSort.InsertionSort(ref MemoryMarshal.GetReference(span), 0, span.Length, less);
}
