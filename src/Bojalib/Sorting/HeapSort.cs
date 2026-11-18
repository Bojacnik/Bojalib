using System.Runtime.InteropServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>In-place heapsort: O(n log n) in every case with no extra memory. Not stable.</summary>
/// <remarks><see langword="null"/>s and NaNs sort first, matching <see cref="Comparer{T}.Default"/>.</remarks>
public static class HeapSort
{
    /// <summary>Sorts <paramref name="span"/> in ascending order using <see cref="IComparable{T}"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements to sort.</param>
    public static void Sort<T>(Span<T> span) where T : IComparable<T>?
    {
        if (Primitives.IsFloatingPoint<T>())
        {
            span = span[Primitives.MoveNaNsToFront(span)..];
        }

        Core(span, default(DefaultLess<T>));
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
        PdqSort.HeapSort(ref MemoryMarshal.GetReference(span), span.Length, less);
}
