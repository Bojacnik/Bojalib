using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bojalib.Sorting.Internal;

namespace Bojalib.Sorting;

/// <summary>
/// Order statistics without a full sort: put the n-th smallest element in place, or sort only the smallest
/// <c>count</c> elements. Average O(n) for <see cref="Select{T}(Span{T}, int)"/>, with an O(n log n) worst-case
/// bound. Uses the same SIMD partitioning as <see cref="QuickSort"/>.
/// </summary>
public static class Selection
{
    private const int InsertionSortThreshold = 24;

    /// <summary>
    /// Rearranges <paramref name="span"/> so the element at <paramref name="index"/> is the one that would be there if
    /// the span were sorted, everything before it is &lt;= it and everything after it is &gt;= it.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="index">The sorted position to fill, e.g. <c>span.Length / 2</c> for the median.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the span.</exception>
    public static void Select<T>(Span<T> span, int index) where T : IComparable<T>?
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, span.Length);

        if (Primitives.IsFloatingPoint<T>())
        {
            int nanCount = Primitives.MoveNaNsToFront(span);
            if (index < nanCount)
            {
                return;
            }

            span = span[nanCount..];
            index -= nanCount;
        }

        Core(span, index, default(DefaultLess<T>));
    }

    /// <summary>
    /// Like <see cref="Select{T}(Span{T}, int)"/>, using <paramref name="comparer"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="index">The sorted position to fill.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the span.</exception>
    public static void Select<T>(Span<T> span, int index, IComparer<T>? comparer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, span.Length);
        Core(span, index, new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default));
    }

    /// <summary>
    /// Like <see cref="Select{T}(Span{T}, int)"/>, using <paramref name="comparison"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="index">The sorted position to fill.</param>
    /// <param name="comparison">The comparison.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the span.</exception>
    public static void Select<T>(Span<T> span, int index, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, span.Length);
        Core(span, index, new ComparisonLess<T>(comparison));
    }

    /// <summary>
    /// Sorts the smallest <paramref name="count"/> elements into the start of <paramref name="span"/>. The order of
    /// the remaining elements is unspecified. Much faster than a full sort when <paramref name="count"/> is small
    /// (e.g. top-k).
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="count">How many of the smallest elements to sort; clamped to the span length.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public static void PartialSort<T>(Span<T> span, int count) where T : IComparable<T>?
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count < span.Length && count > 0)
        {
            Select(span, count - 1);
        }

        QuickSort.SortDefault(span[..Math.Min(count, span.Length)]);
    }

    /// <summary>
    /// Like <see cref="PartialSort{T}(Span{T}, int)"/>, using <paramref name="comparer"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="count">How many of the smallest elements to sort; clamped to the span length.</param>
    /// <param name="comparer">The comparer, or <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public static void PartialSort<T>(Span<T> span, int count, IComparer<T>? comparer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var less = new ComparerLess<T, IComparer<T>>(comparer ?? Comparer<T>.Default);
        if (count < span.Length && count > 0)
        {
            Core(span, count - 1, less);
        }

        PdqSort.Sort(span[..Math.Min(count, span.Length)], less);
    }

    /// <summary>
    /// Like <see cref="PartialSort{T}(Span{T}, int)"/>, using <paramref name="comparison"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="span">The elements.</param>
    /// <param name="count">How many of the smallest elements to sort; clamped to the span length.</param>
    /// <param name="comparison">The comparison.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public static void PartialSort<T>(Span<T> span, int count, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var less = new ComparisonLess<T>(comparison);
        if (count < span.Length && count > 0)
        {
            Core(span, count - 1, less);
        }

        PdqSort.Sort(span[..Math.Min(count, span.Length)], less);
    }

    // Introselect on pdqsort's partitions, with heapsort as the worst-case fallback.
    private static void Core<T, TLess>(Span<T> span, int index, TLess less) where TLess : ILess<T>
    {
        ref T r = ref MemoryMarshal.GetReference(span);
        int begin = 0;
        int end = span.Length;
        bool leftmost = true;
        int budget = 2 * (BitOperations.Log2((uint)span.Length) + 1);

        while (end - begin > InsertionSortThreshold)
        {
            if (--budget == 0)
            {
                PdqSort.HeapSort(ref Unsafe.Add(ref r, begin), end - begin, less);
                return;
            }

            PdqSort.ChoosePivot(ref r, begin, end, less);

            // Pivot equal to the element before the range: [begin, pivot] are all equal, so either the answer is
            // among them or everything up to the pivot can be skipped at once (avoids O(n²) on many duplicates).
            if (!leftmost && !less.Less(Unsafe.Add(ref r, begin - 1), Unsafe.Add(ref r, begin)))
            {
                int equalEnd = PdqSort.PartitionLeft(ref r, begin, end, less);
                if (index <= equalEnd)
                {
                    return;
                }

                begin = equalEnd + 1;
                continue;
            }

            int pivotPos = PdqSort.PartitionRight(ref r, begin, end, less, out _);
            if (pivotPos == index)
            {
                return;
            }

            if (index < pivotPos)
            {
                end = pivotPos;
            }
            else
            {
                begin = pivotPos + 1;
                leftmost = false;
            }
        }

        PdqSort.InsertionSort(ref r, begin, end, less);
    }
}
